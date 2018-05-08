# FsNetMQ

## Example

```fsharp
use router = Socket.router ()
router.bind socket "tcp://*:6566"

use dealer = Socket.dealer ()
Socket.connect socket "tcp://127.0.0.1:6566"

Frame.send dealer "Hello"B

let frame,more = Frame.recv router
```

### Poller

Poller is using IObservable, so when ever you add a socket to the poller you get an observable which you can subscribe to notify of new messages.

```fsharp
use poller = Poller.create ()
use dealer = Socket.dealer ()
use subscriber = Socker.sub ()

// Connecting and subscribing...

let dealerObservable = 
  Poller.addSocket poller dealer
  |> Observable.map Frame.recv
  
let subObservable = 
  Poller.addSocket poller subscriber
  |> Observable.map Frame.recv

use observer = 
  Observable.merge dealerObservable subObservable  
  |> Observable.subscribe (fun msg -> printfn "%A" msg)
  
Poller.run poller
```

### Actor

Actor is a thread with socket attached to it, so you are able to send it messages and request cancellation. Together with Poller it is a powerful concept

```fsharp
Actor.create (fun shim -> 
  use poller = Poller.create ()
    
  // Registering for the end message which will cancel the actor
  use emObserver = Poller.registerEndMessage poller shim

  // Creating sockets and adding them to the poller
  ...
   
  // Signalling that the actor is ready, this will let the Actor.create function to exit
  Actor.signal shim

  Poller.run poller

```
