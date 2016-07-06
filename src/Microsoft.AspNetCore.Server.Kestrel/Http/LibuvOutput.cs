﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Networking;

namespace Microsoft.AspNetCore.Server.Kestrel.Http
{
    public class LibuvOutput
    {
        public const int MaxPooledWriteReqs = 1024;

        public LibuvOutput(
            LibuvThread libuvThread,
            UvStreamHandle socket,
            IReadableChannel outputChannel,
            string connectionId,
        {
            LibuvThread = libuvThread;
            Socket = socket;
            OutputChannel = outputChannel;
            ConnectionId = connectionId;
            Log = log;
            WriteReqPool = libuvThread.WriteReqPool;
        }

        public IKestrelTrace Log { get; }

        public IReadableChannel OutputChannel { get; }

        public UvStreamHandle Socket { get; }

        public LibuvThread LibuvThread { get; }

        public string ConnectionId { get; }

        public Queue<UvWriteReq> WriteReqPool { get; }

        public async Task Start()
        {
            try
            {
                while (!OutputChannel.Completed)
                {
                    await OutputChannel;

                    // Switch to the UV thread
                    await LibuvThread;

                    if (Socket.IsClosed)
                    {
                        break;
                    }

                    if (OutputChannel.Completed)
                    {
                        break;
                    }

                    var span = OutputChannel.BeginRead();
                    var start = span.Begin;
                    var end = span.End;

                    int bytes;
                    int buffers;
                    BytesBetween(start, end, out bytes, out buffers);

                    var req = TakeWriteReq();

                    try
                    {
                        int status = await req.Write(Socket, start, end, buffers);
                        Log.ConnectionWriteCallback(ConnectionId, status);
                    }
                    catch (Exception ex)
                    {
                        Log.ConnectionError(ConnectionId, ex);
                        break;
                    }
                    finally
                    {
                        OutputChannel.EndRead(end);

                        // Return the request to the pool
                        ReturnWriteRequest(req);
                    }
                }
            }
            catch (TaskCanceledException)
            {

            }
            finally
            {
                await LibuvThread;

                try
                {
                    if (!Socket.IsClosed)
                    {
                        var shutdownAwaitable = new LibuvAwaitable<UvShutdownReq>();
                        using (var shutdownReq = new UvShutdownReq(Log))
                        {
                            Log.ConnectionWriteFin(ConnectionId);
                            shutdownReq.Init(LibuvThread.Loop);
                            shutdownReq.Shutdown(Socket, LibuvAwaitable<UvShutdownReq>.Callback, shutdownAwaitable);
                            int status = await shutdownAwaitable;

                            Log.ConnectionWroteFin(ConnectionId, status);
                        }
                    }

                }
                catch (Exception ex)
                {
                    Log.ConnectionError(ConnectionId, ex);
                }

                OutputChannel.CompleteReading();

                Log.ConnectionStop(ConnectionId);
            }
        }

        private UvWriteReq TakeWriteReq()
        {
            UvWriteReq req;

            if (WriteReqPool.Count > 0)
            {
                req = WriteReqPool.Dequeue();
            }
            else
            {
                req = new UvWriteReq(Log);
                req.Init(LibuvThread.Loop);
            }

            return req;
        }

        private void ReturnWriteRequest(UvWriteReq req)
        {
            if (WriteReqPool.Count < MaxPooledWriteReqs)
            {
                WriteReqPool.Enqueue(req);
            }
            else
            {
                req.Dispose();
            }
        }

        private static void BytesBetween(MemoryPoolIterator start, MemoryPoolIterator end, out int bytes, out int buffers)
        {
            if (start.Block == end.Block)
            {
                bytes = end.Index - start.Index;
                buffers = 1;
                return;
            }

            bytes = start.Block.Data.Offset + start.Block.Data.Count - start.Index;
            buffers = 1;

            for (var block = start.Block.Next; block != end.Block; block = block.Next)
            {
                bytes += block.Data.Count;
                buffers++;
            }

            bytes += end.Index - end.Block.Data.Offset;
            buffers++;
        }
    }
}
