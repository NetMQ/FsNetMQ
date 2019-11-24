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
            
type Alt<'x> = AltContext -> Async<'x>
       
let (^=>) alt (f: 'x->Async<'y>) : Alt<'y> =
    fun (ctx:AltContext) ->
        async {
            let! x = alt ctx
            return! f (x)
        }
    
let (^->) alt (f: 'x->'y) : Alt<'y> =
    fun (ctx:AltContext) ->
        async {
            let! x = alt ctx
            return f (x)
        }
    
let (^=>.) alt (y:Async<'y>) : Alt<'y> =
    fun (ctx:AltContext) ->
        async {
            let! _ = alt ctx
            return! y
        }
    
let (^->.) alt (y: 'y) : Alt<'y> =
    fun (ctx:AltContext) ->
        async {
            let! _ = alt ctx
            return y
        }    
        
[<RequireQualifiedAccess>]
type Alt() =
    static member FromEvent e =
        fun (ctx:AltContext) -> async {
            let! args = Async.AwaitEvent e
            do! ctx.Take ()
            return args
        }
        
    static member FromAsync comp =
        fun (ctx:AltContext) -> async {
            let! x = comp
            do! ctx.Take ()
            return x
        }

    static member Ignore alt =
        fun (ctx:AltContext) -> async {
            let! _ = alt ctx
            return ()
        }        
        
    static member Sleep millisecondsDueTime =
        fun (ctx:AltContext) -> async {
            do! Async.Sleep millisecondsDueTime
            do! ctx.Take ()
            return ()
        }        
        
    static member Choose (alts: Alt<'x> list) : Alt<'x> =
        fun ctx ->
            async {
                let! token = Async.CancellationToken                                          
                let promise = new Promise<'x>()
                let computations = List.map (fun alt -> alt ctx) alts
                let mutable cancelledCounter = List.length computations                
                let complete res =                
                    match res with
                    | Choice1Of3 x -> promise.SetResult x
                    | Choice2Of3 (x:exn) -> promise.SetException x
                    | Choice3Of3 (x:OperationCanceledException) ->
                        if Interlocked.Decrement &cancelledCounter = 0 then
                            promise.SetCanceled x
                        
                List.iter (fun cont ->
                    Async.StartWithContinuations (cont,
                                                  (fun res -> complete (Choice1Of3 res)),
                                                  (fun exc -> complete (Choice2Of3 exc)),
                                                  (fun exc -> complete (Choice3Of3 exc)),
                                                  token)) computations
                
                return! promise.Async
                
            }
                    
    static member Parallel (computations: seq<Alt<'T>>) : Alt<'T []> =
        let startImmediate token alt =            
            Async.FromContinuations (fun (cont, _, _) ->
                let ctx = new AltContext()
                let promise = new Promise<'T>()                 
                Async.StartWithContinuations (alt ctx,
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
        
        fun (ctx:AltContext) ->
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
        
    static member ToAsync alt =
        async {
            let ctx = new AltContext()
            return! alt ctx
        }
        
    static member Iterate (state:'T1) (f:'T1->Alt<Choice<'T1,'T2>>) =
        fun (ctx:AltContext) ->
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
                let alt = f state 
                let! x = alt ctx
                complete x
                
                let ctx' = new AltContext()                                                                                                                               
                while Option.isNone result do
                    ctx'.Reset ()
                    let alt = f state 
                    let! x = alt ctx'
                    complete x                    
                
                return Option.get result                    
            }
        
    static member Run (alt, ?cancellationToken:CancellationToken) =
        let ctx = new AltContext()                                                                                                                               
        let runtime = new Runtime()
        runtime.Run (alt ctx, ?cancellationToken=cancellationToken)
    