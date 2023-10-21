using System;
using System.IO.Pipes;

namespace MumbleLinkPlugin
{
    /// <summary>
    /// A child process spawned by the mod to avoid PositionInfoSupplier being halted by GC.
    /// Consumes messages from parent process and passes them to PositionInfoSupplier.
    /// </summary>
    public class PositionInfoConsumer
    {
        static void Main(string[] args)
        {
            var structSize = int.Parse(args[0]);
            var handle = args[1];

            var supplier = new PositionInfoSupplier(structSize);
            supplier.Init();
            supplier.Start();
            ConsumeMessages(supplier, handle);
        }

        private static void ConsumeMessages(PositionInfoSupplier supplier, string handle)
        {
            var pipeClient = new AnonymousPipeClientStream(PipeDirection.In, handle);
            byte[] receiveBuffer = new byte[supplier.structSize];
            while (true)
            {
                var bytesRead = pipeClient.Read(receiveBuffer, 0, receiveBuffer.Length);
                if (bytesRead <= 0)
                    break;

                var message = new byte[bytesRead];
                Buffer.BlockCopy(receiveBuffer, 0, message, 0, bytesRead);
                supplier.OnNewMessage(message);
            }
            supplier.Dispose();
        }
    }
}
