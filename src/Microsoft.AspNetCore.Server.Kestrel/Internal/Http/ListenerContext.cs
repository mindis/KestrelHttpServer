// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Networking;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public class ListenerContext
    {
        public ListenerContext(ServiceContext serviceContext)
        {
            ServiceContext = serviceContext;
        }

        public ServiceContext ServiceContext { get; set; }

        public ListenOptions ListenOptions { get; set; }

        public KestrelThread Thread { get; set; }

        /// <summary>
        /// Creates a socket which can be used to accept an incoming connection.
        /// </summary>
        protected UvStreamHandle CreateAcceptSocket()
        {
            switch (ListenOptions.Type)
            {
                case ListenType.IPEndPoint:
                case ListenType.FileHandle:
                    var tcpHandle = new UvTcpHandle(ServiceContext.Log);
                    tcpHandle.Init(Thread.Loop, Thread.QueueCloseHandle);
                    tcpHandle.NoDelay(ListenOptions.NoDelay);
                    return tcpHandle;
                case ListenType.SocketPath:
                    var pipeHandle = new UvPipeHandle(ServiceContext.Log);
                    pipeHandle.Init(Thread.Loop, Thread.QueueCloseHandle);
                    return pipeHandle;
                default:
                    throw new InvalidOperationException();
            }
        }
    }
}
