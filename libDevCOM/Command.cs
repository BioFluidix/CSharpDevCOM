using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libDevCOM {
    /**
     * A command for sending and receiving data to/from a device.
     *
     * @param <T> the data type, e.g. String or Packet.
     */
    public sealed class Command<T> {

        // final/immutable
        private readonly T data;
        private readonly Action<T> onSent;
        private readonly Action<T> onReceived;
        private readonly bool? replyExpected;
        private readonly Action<T, Exception> onError;
        private readonly Action<string> onCancellationRequested;

        // non-final/mutable
        private volatile TaskCompletionSource<T> reply;
        private volatile bool consumed;
        private volatile bool cancellationRequested;

        /**
         * Creates a new command.
         * @param data data to send
         * @param onSent consumer to call if a data has been sent
         * @param replyExpected predicate that indicates whether a reply is expected
         * @param onReceived consumer to call if a response has been received
         * @param onError consumer to call if an error occurs
         * @param onCancellationRequested consumer called if cancellation has been requested
         */
        public Command(
            T data,
            Action<T> onSent,
            bool? replyExpected,
            Action<T> onReceived,
            Action<T, Exception> onError,
            Action<string> onCancellationRequested) {
            this.data = data;
            this.onSent = onSent;
            this.onReceived = onReceived;
            this.replyExpected = replyExpected;
            this.reply = null;
            this.onError = onError;
            this.onCancellationRequested = onCancellationRequested;
        }

        /**
         * Resets this command for being reused for sending and receiving data.
         */
        //@Deprecated
        // TODO 25.10.2021 would work great but needs more thoughts on identity checks and collections
        public void reset() {
            if (!consumed) throw new Exception(
                "Cannot reset unused command. " +
                    "This command might be enqueued and should not be used more than once per queue");
            this.consumed = false;
            var r = this.reply;
            if (r != null) {
                r.SetException(new Exception("Reset"));
            }
            this.reply = null;
        }

        /**
         * Returns a new Command builder.
         * @param <T>
         * @return a new Command builder
         */
        public static Builder newBuilder() {
            return Builder.newBuilder();
        }

        /**
         * Command builder.
         *
         * @param <T>
         */
        public sealed class Builder {
            private T data;
            private Action<T> onSent;
            private Action<T> onReceived;
            private bool? replyExpected;
            private Action<T, Exception> onError;
            private Action<string> onCancellationRequested;

            private Builder() {

            }

            internal static Builder newBuilder() {
                return new Builder();
            }

            /**
             *
             * @param data data to be sent by the command
             * @return this builder
             */
            public Builder withData(T data) {
                this.data = data;
                return this;
            }

            /**
             *
             * @param onSent called if/when the data has been sent (may be {@code null}).
             * @return this builder
             */
            public Builder withOnSent(Action<T> onSent) {
                this.onSent = onSent;
                return this;
            }

            /**
             *
             * @param onReceived called if/when the data has been received (may be {@code null}).
             * @return this builder
             */
            public Builder withOnReceived(Action<T> onReceived) {
                this.onReceived = onReceived;
                return this;
            }

            /**
             *
             * @param replyExpected defines whether a reply is expected.
             * @return this builder
             */
            public Builder withReplyExpected(bool replyExpected) {
                this.replyExpected = replyExpected;
                return this;
            }

            /**
             *
             * @param onError error handler is called whenever an error occurs (may be {@code null}).
             * @return this builder
             */
            public Builder withOnError(Action<T, Exception> onError) {
                this.onError = onError;

                return this;
            }


            /**
             *
             * @param onCancellationRequested handler is called if cancellation is requested (may be {@code null}).
             * @return this builder
             */
            public Builder withOnCancellationRequested(Action<string> onCancellationRequested) {
                this.onCancellationRequested = onCancellationRequested;

                return this;
            }

            /**
             * Creates a new command.
             * @return new command
             */
            public Command<T> build() {
                return new Command<T>(data, onSent, replyExpected, onReceived, onError, onCancellationRequested);
            }

        }

        /**
         * Indicates whether a reply is expected by this command.
         * @return {@code true} if a reply is expected; {@code false} otherwise
         */
        public bool isReplyExpected() {
            return (bool)(replyExpected == null ? true : replyExpected);
        }

        /**
         * Returns the future reply.
         * @return reply
         */
        public TaskCompletionSource<T> getReply() {

            lock (this) {
                if (reply == null) {
                    reply = new TaskCompletionSource<T>();
                }
            }

            return reply;
        }

        /**
         * Returns the data to send.
         * @return the data to send
         */
        public T getData() {
            return data;
        }


        /**
         * Indicates whether this command has been consumed.
         * @return {@code if this command has been consumed}; {@code false} otherwise
         */
        public bool isConsumed() {
            return consumed;
        }

        /**
         * Requests command cancellation.
         */
        public void requestCancellation() {
            this.cancellationRequested = true;
        }

        /**
         * Indicates whether cancellation has been requested.
         * @return {@code if cancellation has been requested}; {@code false} otherwise
         */
        public bool isCancellationRequested() {
            return cancellationRequested;
        }

        // ---------------------------------------------------------
        // PRIVATE METHODS
        // ---------------------------------------------------------

        /*pkg private*/
        internal Action<T> getOnReceived() {
            return onReceived;
        }
        /*pkg private*/
        internal Action<T> getOnSent() {
            return onSent;
        }

        /*pkg private*/
        internal Action<string> getOnHandleCancellationRequest() {
            return onCancellationRequested;
        }

        /*pkg private*/
        internal Action<T, Exception> getOnError() {
            return onError;
        }

        /*pkg private*/
        internal void consume() {
            this.consumed = true;
        }

        public override string ToString() {
            return "[cmd: " + "data=" + (data == null ? "<no data>" : data) + "]";
        }
    }
}
