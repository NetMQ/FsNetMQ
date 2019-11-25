[<AutoOpen>]
module FsNetMQ.Alt

open FsNetMQ
open System
open System.Threading

type AltContext() =
    let mutable counter = 0    
    
    member this.Acquire () = Monitor.Enter (this)
    member this.Release () = Monitor.Exit (this)
    
    member this.TakeRelease() =
        Async.FromContinuations (fun (cont, _, ccont) ->                        
            let first = counter = 0
            counter <- counter + 1
            this.Release()
            
            if first then
                cont ()
            else
                ccont <| new OperationCanceledException("operation cancelled")
        )
        
    member this.Take () =                
        Async.FromContinuations (fun (cont, _, ccont) ->            
            this.Acquire()
            let first = counter = 0
            counter <- counter + 1
            this.Release()
            
            if first then
                cont ()
            else
                ccont <| new OperationCanceledException("operation cancelled")
            )
    member this.Reset () = counter <- 0
            
type Alt<'x> = Alt of (AltContext -> Async<'x>)*Async<'x> 
    
let internal makeAlt (alt:AltContext->Async<'x>) (comp:Async<'x>) = Alt (alt,comp)    
         
type Microsoft.FSharp.Control.AsyncBuilder with
    member inline this.Bind(Alt (_,comp): Alt<'T>, binder: ('T -> Async<'U>)) : Async<'U> =
        this.Bind(comp, binder)        
                           
    member inline this.ReturnFrom (Alt (_,comp):Alt<'T>) : Async<'T> = comp                      
         
         
let (^=>) (Alt (alt, comp):Alt<'x>) (f:'x->Async<'y>) =
    let alt = fun (ctx:AltContext) ->
        async.Bind (alt ctx, f)
    
    let comp = async.Bind (comp, f)
    
    Alt (alt, comp)

let (^=>.) (Alt (alt, comp)) (y:Async<'y>) =
    let alt = fun (ctx:AltContext) ->
        async.Bind (alt ctx, fun _ -> y)
    
    let comp = async.Bind (comp, fun _ -> y)
    
    Alt (alt, comp)
    
let (^==>) (Alt (alt, comp):Alt<'x>) (f:'x->Alt<'y>) =
    let alt = fun (ctx:AltContext) ->
        async.Bind (alt ctx, fun x ->
            let (Alt (_,comp)) = f x
            comp
            )
    
    let comp = async.Bind (comp, fun x ->
        let (Alt (_,comp)) = f x
        comp
        )
    
    Alt (alt, comp)

let (^==>.) (Alt (alt, comp)) (Alt(_,y):Alt<'y>) =
    let alt = fun (ctx:AltContext) ->
        async.Bind (alt ctx, fun _ -> y)
    
    let comp = async.Bind (comp, fun _ -> y)
    
    Alt (alt, comp)
                    
let (^->) (Alt (alt, comp)) (f: 'x->'y) : Alt<'y> =
    let alt = fun (ctx:AltContext) ->
        async {
            let! x = alt ctx
            return f (x)
        }
    
    let comp = async {
        let! x = comp
        return f (x)
    }
    
    makeAlt alt comp   
           
let (^->.) (Alt (alt, comp)) (y: 'y) : Alt<'y> =
    let alt = fun (ctx:AltContext) ->
        async {
            let! _ = alt ctx
            return y
        }
        
    let comp = async {
        let! _ = comp
        return y
    }
    
    makeAlt alt comp    
        
[<RequireQualifiedAccess>]
type Alt =
    static member FromEvent e =
        let alt = fun (ctx:AltContext) -> async {
            let! args = Async.AwaitEvent e
            do! ctx.Take ()
            return args
        }
        
        let comp = Async.AwaitEvent e
        
        makeAlt alt comp
        
    static member FromAsync comp =
        let alt = fun (ctx:AltContext) -> async {
            let! x = comp
            do! ctx.Take ()
            return x
        }              
        
        makeAlt alt comp

    static member Ignore (Alt (alt, comp)) =
        let alt = fun (ctx:AltContext) -> async {
            let! _ = alt ctx
            return ()
        }
        
        let comp = Async.Ignore comp
        
        makeAlt alt comp        
        
    static member Sleep millisecondsDueTime =
        let alt = fun (ctx:AltContext) -> async {
            do! Async.Sleep millisecondsDueTime
            do! ctx.Take ()
            return ()
        }
        
        let comp = Async.Sleep millisecondsDueTime
        
        makeAlt alt comp
        
    static member Choose (alts: Alt<'x> list) : Alt<'x> =
        let alt = fun ctx ->
            async {
                let! token = Async.CancellationToken                                          
                let promise = new Promise<'x>()
                let computations = List.map (fun (Alt (alt,_)) -> alt ctx) alts
                let mutable cancelledCounter = List.length computations                
                let complete res =                
                    match res with
                    | Choice1Of3 x -> promise.SetResult x
                    | Choice2Of3 (x:exn) -> promise.SetException x
                    | Choice3Of3 (x:OperationCanceledException) ->
                        if Interlocked.Decrement &cancelledCounter = 0 then
                            promise.SetCanceled x
                        
                List.iter (fun comp ->
                    Async.StartWithContinuations (comp,
                                                  (fun res -> complete (Choice1Of3 res)),
                                                  (fun exc -> complete (Choice2Of3 exc)),
                                                  (fun exc -> complete (Choice3Of3 exc)),
                                                  token)) computations
                
                return! promise.Async                
            }
        
        let comp = async {
            let ctx = new AltContext()
            return! alt ctx
        }
        
        makeAlt alt comp
                    
    static member Parallel (computations: seq<Alt<'T>>) : Alt<'T []> =
        let startImmediate token (Alt (_, comp)) =            
            Async.FromContinuations (fun (cont, _, _) ->
                let promise = new Promise<'T>()                 
                Async.StartWithContinuations (comp,
                                              promise.SetResult,
                                              promise.SetException,
                                              promise.SetCanceled,
                                              token)
                cont promise.Async  
            )
            
        let folder a b =
            async.Bind (a, fun xs ->
                async {
                    let! x = b
                    return x :: xs
                }
            )
        
        let alt = fun (ctx:AltContext) ->
            async {
               do! ctx.Take()
               let! token = Async.CancellationToken
               let! tasks =
                   computations
                   |> Seq.map (startImmediate token)
                   |> Seq.fold folder (async.Return([]))
               let! results = Seq.fold folder (async.Return([])) tasks
               return List.toArray results                            
            }
            
        let comp = async {
            let! token = Async.CancellationToken
            let! tasks =
               computations
               |> Seq.map (startImmediate token)
               |> Seq.fold folder (async.Return([]))
            let! results = Seq.fold folder (async.Return([])) tasks
            return List.toArray results            
        }
        
        makeAlt alt comp
        
    static member Await (Alt (_, comp)) = comp
        
    static member Iterate (state:'T1) (f:'T1->Alt<Choice<'T1,'T2>>) =
        let alt = fun (ctx:AltContext) ->
            async {                                
                let mutable state = state            
                let mutable result = None
                
                let complete x =
                    match x with
                    | Choice1Of2 x->
                        state <- x
                    | Choice2Of2 x->
                        result <- Some x
                
                // The first run is with the provided alt-context, continueing with a new one for every loop 
                let (Alt (alt,_)) = f state 
                let! x = alt ctx
                complete x
                
                let ctx' = new AltContext()                                                                                                                               
                while Option.isNone result do
                    ctx'.Reset ()
                    let (Alt (alt,_)) = f state 
                    let! x = alt ctx'
                    complete x                    
                
                return Option.get result                    
            }
            
        let comp = async {
            let ctx = new AltContext()
            return! alt ctx
        }
        
        makeAlt alt comp
                
    static member Run (Alt (alt, _), ?cancellationToken:CancellationToken) =
        let ctx = new AltContext()                                                                                                                               
        let runtime = new Runtime()
        runtime.Run (alt ctx, ?cancellationToken=cancellationToken)
        
    static member Run(comp, ?cancellationToken:CancellationToken) =
        let runtime = new Runtime()
        runtime.Run (comp, ?cancellationToken=cancellationToken)

