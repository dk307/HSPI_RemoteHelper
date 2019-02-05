using System;
using System.Net.Http;

namespace Rssdp.Infrastructure
{
    /// <summary>
    /// Provides arguments for the <see cref="ISsdpCommunicationsServer.ResponseReceived"/> event.
    /// </summary>
    internal sealed class ResponseReceivedEventArgs : EventArgs
    {
        #region Fields

        private readonly HttpResponseMessage _Message;
        private readonly UdpEndPoint _ReceivedFrom;

        #endregion Fields

        #region Constructors

        /// <summary>
        /// Full constructor.
        /// </summary>
        /// <param name="message">The <see cref="HttpResponseMessage"/> that was received.</param>
        /// <param name="receivedFrom">A <see cref="UdpEndPoint"/> representing the sender's address (sometimes used for replies).</param>
        public ResponseReceivedEventArgs(HttpResponseMessage message, UdpEndPoint receivedFrom)
        {
            _Message = message;
            _ReceivedFrom = receivedFrom;
        }

        #endregion Constructors

        #region Public Properties

        /// <summary>
        /// The <see cref="HttpResponseMessage"/> that was received.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public HttpResponseMessage Message
        {
            get { return _Message; }
        }

        /// <summary>
        /// The <see cref="UdpEndPoint"/> the response came from.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public UdpEndPoint ReceivedFrom
        {
            get { return _ReceivedFrom; }
        }

        #endregion Public Properties
    }
}