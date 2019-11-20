[<AutoOpen>]
module FsNetMQ.Alt

open FsNetMQ
open System
open System.Threading
open System.Threading.Tasks

type AltContext() =
    let mutable counter = 0
    
    member this.Return (x) =                
        Async.FromContinuations (fun (cont, _, ccont) ->
            if Interlocked.Increment &counter = 1 then
                cont x
            else
                ccont <| new OperationCanceledException("operation cancelled")
            )
            
type Alt<'x> =
    private
    | Alt of (AltContext -> Async<'x>)
       
let (^=>) (Alt alt) (f: 'x->Async<'y>) : Alt<'y> =
    fun (ctx:AltContext) ->
        async {
            let! x = alt ctx
            return! f (x)
        }
    |> Alt
    
let (^->) (Alt alt) (f: 'x->'y) : Alt<'y> =
    fun (ctx:AltContext) ->
        async {
            let! x = alt ctx
            return f (x)
        }
    |> Alt
    
let (^=>.) (Alt alt) (y:Async<'y>) : Alt<'y> =
    fun (ctx:AltContext) ->
        async {
            let! _ = alt ctx
            return! y
        }
    |> Alt
    
let (^->.) (Alt alt) (y: 'y) : Alt<'y> =
    fun (ctx:AltContext) ->
        async {
            let! _ = alt ctx
            return y
        }
    |> Alt
        
[<RequireQualifiedAccess>]
module Alt = 
    let fromEvent e =
        fun (ctx:AltContext) -> async {
            let! args = Async.AwaitEvent e
            return! ctx.Return args
        }
        |> Alt
        
    let fromAsync comp =
        fun (ctx:AltContext) -> async {
            let! x = comp
            return! ctx.Return x
        }
        |> Alt

    let ignore (Alt alt) =
        fun (ctx:AltContext) -> async {
            let! _ = alt ctx
            return ()
        }
        |> Alt
        
    let choose (alts: Alt<'x> list) : Alt<'x> =
        fun ctx ->
            async {
                let! token = Async.CancellationToken                                          
                let promise = new Promise<'x>()
                let computations = List.map (fun (Alt alt) -> alt ctx) alts
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
        |> Alt            
        
    let toAsync (Alt alt) =
        async {
            let ctx = new AltContext()
            return! alt ctx
        }                   
        
        
        
    