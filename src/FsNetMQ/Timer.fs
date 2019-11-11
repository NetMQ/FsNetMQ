[<RequireQualifiedAccess;CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module FsNetMQ.Timer

open NetMQ

type T = FsNetMQ.Timer
                                   
let create (interval:int<milliseconds>) = Timer (new NetMQTimer (int interval))
let disable (Timer t) = t.Enable <- false
let enable (Timer t) = t.Enable <- true
let isEnabled (Timer t) = t.Enable
let reset (Timer t) = t.EnableAndReset ()