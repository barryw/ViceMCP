namespace ViceMCP.ViceBridge.Exceptions
{
    /// <summary>
    /// 
    /// </summary>
    public class SocketDisconnectedException : Exception
    {
        /// <summary>
        /// Creates an instance of <see cref="SocketDisconnectedException"/>
        /// </summary>
        public SocketDisconnectedException()
        {
        }
        /// <summary>
        /// Creates an instance of <see cref="SocketDisconnectedException"/>
        /// </summary>
        /// <param name="message"></param>
        public SocketDisconnectedException(string? message) : base(message)
        {
        }
        /// <summary>
        /// Creates an instance of <see cref="SocketDisconnectedException"/>
        /// </summary>
        /// <param name="message"></param>
        /// <param name="innerException"></param>
        public SocketDisconnectedException(string? message, Exception? innerException) : base(message, innerException)
        {
        }
    }
}