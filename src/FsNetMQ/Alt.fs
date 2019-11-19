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
                let computations = List.map (fun (Alt alt) -> alt ctx) alts 
                                            
                let source = TaskCompletionSource<'x>()                            
                let complete res =                
                    match res with
                    | Choice1Of2 x -> source.SetResult x
                    | Choice2Of2 (x:exn) -> source.SetException x                                                                           
                        
                List.iter (fun cont ->
                    Async.StartWithContinuations (cont,
                                                  (fun res -> complete (Choice1Of2 res)),
                                                  (fun exc -> complete (Choice2Of2 exc)),
                                                  (fun _ -> ()))) computations
                
                return! Async.AwaitTask source.Task
            }
        |> Alt            
        
    let toAsync (Alt alt) =
        async {
            let ctx = new AltContext()
            return! alt ctx
        }                   
        
        
        
    