[<RequireQualifiedAccess;CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module FsNetMQ.Stream

open System

type T = FsNetMQ.Stream                                                            

let create size = Stream (Array.create size 0uy,0)    
let getBuffer (Stream (buffer, _)) = buffer
let getOffset (Stream (_, offset)) = offset

let recv socket =
    let buffer, more = Frame.recv socket
    Stream (buffer, 0), more
    
let tryRecv socket timeout =
    match Frame.tryRecv socket timeout with
    | Some (buffer, more) -> Some (Stream(buffer,0), more)
    | None -> None
    
let send socket (Stream (buffer, offset)) =
    let frame = 
        if Array.length buffer = offset then
            buffer
        else
            Array.sub buffer 0 offset
            
    Frame.send socket frame

let bytesLeft (Stream (buffer, offset)) = (Array.length buffer) - offset    
let isEnoughBytesLeft s bytes = bytesLeft s >= bytes  

let reset (Stream (buffer, _)) = Stream (buffer, 0)
let advance (Stream (buffer, offset)) by = Stream (buffer, offset + by)            
let extendBy stream by =
    match isEnoughBytesLeft stream by with
    | true -> stream
    | _ -> 
        let oldBuffer = getBuffer stream
        let offset = getOffset stream
        let oldLength = Array.length oldBuffer
                    
        let newLength = if oldLength > by then oldLength * 2 else oldLength + by
        
        let newBuffer = Array.create newLength 0uy 
        
        Buffer.BlockCopy (oldBuffer,0, newBuffer,0, offset)
        Stream (newBuffer, offset)
    
module Reader = 
    type Reader<'a> = T -> 'a option*T
    
    type ReaderBuilder() = 
        member this.Bind (r,f) =
            fun (stream:T) ->
                let x, stream = r stream
                match x with
                | None -> None, stream 
                | Some x -> f x stream 
        member this.Return x = fun buffer -> Some x,buffer
        member this.Yield x = fun buffer -> Some x,buffer
        member this.YieldFrom (r:Reader<'a>) = fun (stream:T) -> r stream
        member this.For (seq,body) = 
            fun (stream:T) -> 
                let xs,stream = Seq.fold (fun (list, stream) i -> 
                                    match list with 
                                    | None -> None, stream
                                    | Some list -> 
                                        let x,stream = body i stream
                                        match x with 
                                        | None -> None,stream
                                        | Some x -> Some (x :: list),stream                                                                                                     
                                ) (Some [], stream) seq
                match xs with 
                | None -> None, stream
                | Some xs -> Some (Seq.rev xs), stream 
                                                
    let reader = new ReaderBuilder()        
                 
    let run (r:Reader<'a>) buffer = 
        let x,_ = r buffer
        x
        
    let check b stream =
        if b then Some (),stream else None, stream
        
    let checkEnoughBytes size stream = 
        if isEnoughBytesLeft stream size then Some (), stream else None, stream            

let writeNumber1 (n:byte) stream =
    let stream = extendBy stream 1 
    let offset = getOffset stream        
    let buffer = getBuffer stream
    buffer.[offset] <- n
    advance stream 1
    
let readNumber1 stream =
    match isEnoughBytesLeft stream 1 with
    | false -> None, stream
    | _ ->
        let buffer = getBuffer stream
        let offset = getOffset stream             
        let x = buffer.[offset]
        Some x, (advance stream 1)                     
    
let writeNumber2 (n:UInt16) stream =
    let stream = extendBy stream 2 
    let offset = getOffset stream        
    let buffer = getBuffer stream
 
    buffer.[offset] <- byte ((n >>> 8) &&& 255us)
    buffer.[offset+1] <- byte (n &&& 255us)        
    advance stream 2
    
let readNumber2 stream =           
    match isEnoughBytesLeft stream 2 with
    | false -> None, stream
    | _ -> 
            let buffer = getBuffer stream
            let offset = getOffset stream
            let number = Some (
                                ((uint16 (buffer.[offset])) <<< 8) + 
                                (uint16 buffer.[offset+1]))
            
            number, (advance stream 2)
         
let writeNumber4 (n:UInt32) stream =
    let stream = extendBy stream 4 
    let offset = getOffset stream        
    let buffer = getBuffer stream  
    buffer.[offset] <- byte ((n >>> 24) &&& 255u)
    buffer.[offset+1] <- byte ((n >>> 16) &&& 255u) 
    buffer.[offset+2] <- byte ((n >>> 8) &&& 255u) 
    buffer.[offset+3] <- byte ((n) &&& 255u)
    advance stream 4                           
         
let readNumber4 stream =        
    match isEnoughBytesLeft stream 4 with
    | false -> None, stream
    | _ ->
        let buffer = getBuffer stream
        let offset = getOffset stream 
        let number = Some (
                            ((uint32 (buffer.[offset])) <<< 24) +
                            ((uint32 (buffer.[offset+1])) <<< 16) +
                            ((uint32 (buffer.[offset+2])) <<< 8) +
                            (uint32 buffer.[offset+3]))
        
        number, (advance stream 4)                                                        
    
let writeNumber8 (n:UInt64) stream =
    let stream = extendBy stream 8 
    let offset = getOffset stream        
    let buffer = getBuffer stream  
    buffer.[offset] <- (byte) ((n >>> 56) &&& 255UL)
    buffer.[offset+1] <- (byte) ((n >>> 48) &&& 255UL)
    buffer.[offset+2] <- (byte) ((n >>> 40) &&& 255UL)
    buffer.[offset+3] <- (byte) ((n >>> 32) &&& 255UL)
    buffer.[offset+4] <- (byte) ((n >>> 24) &&& 255UL) 
    buffer.[offset+5] <- (byte) ((n >>> 16) &&& 255UL)
    buffer.[offset+6] <- (byte) ((n >>> 8)  &&& 255UL)
    buffer.[offset+7] <- (byte) ((n)       &&& 255UL)
                    
    advance stream 8
    
let readNumber8 stream =            
    match isEnoughBytesLeft stream 8 with
    | false -> None, stream
    | _ ->
        let buffer = getBuffer stream
        let offset = getOffset stream 
        let number = Some (
                            ((uint64 (buffer.[offset]) <<< 56)) +
                            ((uint64 (buffer.[offset+1]) <<< 48)) +
                            ((uint64 (buffer.[offset+2]) <<< 40)) +
                            ((uint64 (buffer.[offset+3]) <<< 32)) +
                            ((uint64 (buffer.[offset+4]) <<< 24)) +
                            ((uint64 (buffer.[offset+5]) <<< 16)) +
                            ((uint64 (buffer.[offset+6]) <<< 8)) +
                            (uint64 buffer.[offset+7]))
            
        number, (advance stream 8)         
    
let writeBytes (host:byte[]) size stream =
    let stream = extendBy stream size 
    let offset = getOffset stream
    let buffer = getBuffer stream
    System.Buffer.BlockCopy (host, 0, buffer, offset, size)
    advance stream size        
    
let readBytes size (Stream (buffer,offset)) =
    match offset + size > Array.length buffer with
    | true -> None, Stream (buffer, offset)
    | _ -> 
        let bytes = Array.sub buffer offset size
        Some bytes, Stream (buffer, offset + size)                 
    
let writeString (host:string) stream =
    let length = System.Text.Encoding.UTF8.GetByteCount(host)        
    let length' = if length > 255 then 255 else length                        
    
    let writeString' stream =
        let stream = extendBy stream length'
        let offset = getOffset stream
        let buffer = getBuffer stream
        System.Text.Encoding.UTF8.GetBytes(host, 0, length', buffer, offset) |> ignore
        advance stream length'
    
    stream 
    |> writeNumber1 (byte length')
    |> writeString'         
        
let readString =
    let readString' length stream =
        let buffer = getBuffer stream
        let offset = getOffset stream 
        let value = System.Text.Encoding.UTF8.GetString (buffer, offset, length)            
        Some value, (advance stream length)

    Reader.reader {
        do! Reader.checkEnoughBytes 1
        let! length = readNumber1            
        do! Reader.checkEnoughBytes (int length)
        let! string = readString' (int length)
        
        return string
    }                                                       
    
let writeLongString (host:string) stream =
    let length = System.Text.Encoding.UTF8.GetByteCount(host)        
                                    
    let writeString' stream =
        let stream = extendBy stream length
        let offset = getOffset stream
        let buffer = getBuffer stream
        System.Text.Encoding.UTF8.GetBytes(host, 0, length, buffer, offset) |> ignore
        advance stream length
    
    stream 
    |> writeNumber4 (uint32 length)
    |> writeString'       
        
let readLongString =
    let readString' length stream =
        let buffer = getBuffer stream
        let offset = getOffset stream
        let value = System.Text.Encoding.UTF8.GetString (buffer, offset, length) 
        Some value, (advance stream length)

    Reader.reader {
        do! Reader.checkEnoughBytes 4
        let! length = readNumber4
        do! Reader.checkEnoughBytes (int length)
        let! string = readString' (int length)
        
        return string
    }               
    
let writeStrings strings stream =
    let stream = writeNumber4 (uint32 (Seq.length strings)) stream 
    List.fold (fun state value -> writeLongString value state) stream strings
    
let readStrings =    
    let readStrings = Reader.reader {
        let! length = readNumber4                        
        for _ in [1ul..length] do                    
            yield! readLongString                                                                                                      
    }
    
    Reader.reader {
        let! strings = readStrings                                    
        return List.ofSeq strings
    }                                               
        
let writeMap map s =             
    let s' = writeNumber4 (uint32 (Seq.length map)) s
    Map.fold (fun b key value -> b |> writeString key |> writeLongString value) s' map
                    
let readMap =                             
       let readPairs =
           Reader.reader {
               let! length = readNumber4                   
                                  
               for _ in [1ul..length] do
                   let! key = readString
                   let! value = readLongString
                   
                   yield (key,value)                                                              
           }
               
       Reader.reader {
           let! pairs = readPairs
           return Map.ofSeq pairs
       }