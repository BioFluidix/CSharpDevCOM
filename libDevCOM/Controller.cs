using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using libDevCOM;
using NLog;

namespace libDevCOM {
    public class Controller<T, V> : IDisposable where V : DataConnection<T, V> {


        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();
        private readonly object simpleLock = new object();
        private volatile ConcurrentQueue<Command<T>> cmdQueue = new ConcurrentQueue<Command<T>>();
        private volatile ConcurrentDictionary<Command<T>, long> replyQueue = new ConcurrentDictionary<Command<T>, long>();
        private volatile ExecutorService cmdExecutor;
        private volatile ExecutorService executor;
        /*pkg private*/
        private volatile TaskCompletionSource queueTaskFuture;
        private volatile Thread queueThread;
        private volatile long cmdTimeout = 0/*no timeout, unit: ms*/;
        private volatile long dataTimeout = 0/*no timeout, unit: ms*/;
        private volatile Action<ThreadInterruptedException> onInterrupted;


        private static long Timestamp()
            => Stopwatch.GetTimestamp() / (Stopwatch.Frequency / 1000_000_000);


        public void Dispose() {
            throw new NotImplementedException();
        }


        /**
         * Creates a new controller instance.
         */
        public Controller() {
            this.cmdExecutor = Executors.newSingleThreadExecutor();
            this.executor = Executors.newCachedThreadPool();
        }

        /**
         * Creates a new controller instance.
         * @param onInterrupted the consumer to call if the command queue thread is interrupted (may be null)
         */
        public Controller(Action<ThreadInterruptedException> onInterrupted) {
            setOnInterrupted(onInterrupted);
            this.cmdExecutor = Executors.newSingleThreadExecutor();
        }

        /**
         * Sets the command timeout (until the command is sent).
         * @param milliseconds duration in milliseconds (0 means no timeout)
         * @return this controller
         */
        public Controller<T, V> setCommandTimeout(long milliseconds) {
            this.cmdTimeout = milliseconds;
            return this;
        }

        /**
         * Sets the data timeout (until the command is sent).
         * @param milliseconds duration in milliseconds (0 means no timeout)
         * @return this controller
         */
        public Controller<T, V> setDataTimeout(long milliseconds) {
            this.dataTimeout = milliseconds;
            return this;
        }

        /**
         * Specifies the consumer to call if the command queue thread is interrupted.
         * @param onInterrupted the consumer to call (may be null)
         */
        public void setOnInterrupted(Action<ThreadInterruptedException> onInterrupted) {
            this.onInterrupted = onInterrupted;
        }

        /**
         * Initializes this controller.
         *
         * @param dataConnection the data connection to use for communication
         */
        public void init(DataConnection<T, V> dataConnection) {

            Action<T> onDataReceived = msg => {
                // find first element that matches reply
                var cmd = replyQueue.Keys.FirstOrDefault(c => dataConnection.getFormat().isReply(c, msg), null);
                if (cmd != null) {
                    replyQueue.Remove(cmd, out long value);
                    cmd.getReply().SetResult(msg);
                    if (cmd.getOnReceived() != null) {
                        cmd.getOnReceived()(msg);
                    }
                }
            };

            dataConnection.setOnDataReceived(onDataReceived);

            dataConnection.setOnIOError((conn, e1) => {

                logger.Warn("cancelling " + replyQueue.Count + " commands due to i/o-error event");

                foreach (var cmd in replyQueue.Keys) {
                    cmd.requestCancellation();
                    cmd.getReply().SetException(new Exception("Cancelling. I/O error occurred.", e1));
                }
                replyQueue.Clear();

            });


            dataConnection.setOnConnectionClosed(o => {
                logger.Warn("cancelling " + replyQueue.Count + " commands due to connection-closed event");

                foreach (var cmd in replyQueue.Keys) {
                    cmd.requestCancellation();
                    cmd.getReply().SetException(new Exception("Cancelling. Connection closed."));
                }
                replyQueue.Clear();

            });

            if (queueThread != null) {
                queueThread.Interrupt();
            }

            queueThread = new Thread(() => {
                var isInterrupted = false;
                while (!isInterrupted) {
                    Command<T> cmdImmutable = null;
                    try {

                        if (!cmdQueue.TryDequeue(out Command<T> cmd)) {
                            cmd = null;
                        }
                        cmdImmutable = cmd;
                        if (cmd == null) {
                            lock (simpleLock) {
                                Monitor.Wait(simpleLock, 1000 /*ms*/);
                            }
                            continue; // nothing to process
                        }


                        TaskCompletionSource cmdFuture = new TaskCompletionSource();
                        Interlocked.Exchange(ref queueTaskFuture, cmdFuture);

                        if (cmdExecutor == null) cmdExecutor = new SingleThreadExecutor(); // Executors.newSingleThreadExecutor();
                        cmdExecutor.execute(() => {
                            // don't process consumed commands
                            if (cmd.isConsumed()) {
                                return;
                            }
                            if (cmd.isCancellationRequested()) {
                                cmd.getReply().SetException(
                                    new Exception("Command '" + cmd + "' cancelled.")
                                );
                                cancelCmd(cmd);
                            }
                            else {
                                if (cmd.isReplyExpected()) {
                                    replyQueue[cmd] = Timestamp();
                                    // ensure result is invalidated if timeout exceeded
                                    if (cmdTimeout > 0) {
                                        if (executor == null) executor = Executors.newCachedThreadPool();
                                        CompletableFuture.delayedExecutor(cmdTimeout, TimeUnit.MILLISECONDS, executor)
                                            .execute(() => {
                                                if (cmd.getReply().Task.IsCompletedSuccessfully || cmd.getReply().Task.IsCanceled) return;

                                                cmd.getReply().SetException(new TimeoutException());
                                                replyQueue.removeFirstOccurrence(cmd);
                                            });
                                    }
                                }
                                else {
                                    cmd.getReply().SetResult(null);
                                }

                                T msg = cmd.getData();
                                try {
                                    dataConnection.writeData(msg);
                                    cmd.consume();
                                    if (cmd.getOnSent() != null) {
                                        try {
                                            cmd.getOnSent()(msg);
                                        }
                                        catch (Exception ex) {
                                            // exception handled by 'onError'
                                            throw new Exception(ex);
                                        }
                                    }
                                }
                                catch (Exception e) {
                                    // cmd identity important to find the command in the reply queue
                                    // thus, we shouldn't allow cmd.reset() without ensuring that the
                                    // cmd is not in the reply queue. For now, we deprecate the reset()
                                    // method.
                                    replyQueue.Remove(cmd, out long timestamp);
                                    cmdFuture.SetException(e);
                                    cancelCmd(cmd);
                                    if (cmd.isReplyExpected()) cmd.getReply().SetException(e);
                                    if (cmd.getOnError() != null) {
                                        try {
                                            cmd.getOnError()(msg, e);
                                        }
                                        catch (Exception ex) {
                                            // exception handled by 'onError'
                                            throw new Exception(e);
                                        }
                                    }
                                    else {
                                        throw new Exception(e);
                                    }
                                }
                            }

                            cmdFuture.complete(null);
                        });

                        if (cmdTimeout == 0) {
                            cmdFuture.Task.Wait();
                            cmdImmutable = null;
                        }
                        else {
                            cmdFuture.Task.Wait(TimeSpan.FromMilliseconds(cmdTimeout));
                            cmdImmutable = null;
                        }
                    }
                    catch (ThreadInterruptedException ex) {
                        Thread.CurrentThread.Interrupt();
                        if (onInterrupted != null) {
                            onInterrupted(ex);
                        }
                        if (cmdImmutable != null) cmdImmutable.getReply().SetException(ex);
                    }
                    catch (TimeoutException ex) {
                        if (cmdImmutable != null) cmdImmutable.getReply().SetException(ex);
                    }
                    catch (Exception ex) {
                        if (cmdImmutable != null) cmdImmutable.getReply().SetException(ex);
                    }
                }
            });
            queueThread.Start();
        }

        private void cancelCmd(Command<T> cmd) {
            var onCancel = cmd.getOnHandleCancellationRequest();
            if (onCancel != null) {
                try {
                    onCancel("Cancellation requested via cmd");
                }
                catch (Exception ex) {
                    if (cmd.getOnError() != null) {
                        cmd.getOnError()(cmd.getData(), ex);
                    }
                    else {
                        logger.Error(ex, "Cannot send command: {}", cmd.getData());
                    }
                }
            }
        }

        /**
         * Closes this controller (also shuts down schedulers/executors).
         */
        public override void close() {
            try {
                if (queueThread != null) {
                    queueThread.Interrupt();
                    queueThread = null;
                }
            }
            finally {

                try {
                    if (cmdExecutor == null) return;
                    try {
                        cmdExecutor.shutdown();
                    }
                    finally {
                        cmdExecutor = null;
                    }
                }
                finally {
                    if (executor == null) return;
                    try {
                        executor.shutdown();
                    }
                    finally {
                        executor = null;
                    }
                }

                var staleCMDQueue = Interlocked.Exchange(ref cmdQueue, new ConcurrentQueue<Command<T>>());
                foreach (var cmd in staleCMDQueue) {
                    try {
                        cmd.requestCancellation();
                    }
                    catch (Exception ex) {
                        logger.Error(ex, "Command cancellation error");
                    }

                }
                staleCMDQueue = null;


                foreach (var cmd in replyQueue.Keys) {
                    try {
                        cmd.requestCancellation();
                        cmd.getReply().SetException(
                            new Exception("Cancellation requested. Controller shutdown.")
                        );
                    }
                    catch (Exception ex) {
                        logger.Error(ex, "Command cancellation error");
                    }

                }
                replyQueue.Clear();
            }
        }

        /**
         * Closes this controller. This method blocks until the controller is closed
         * (all tasks have been executed), or the timeout occurs. If no timeout is specified
         * the associated executor will shutdown immediately.
         * @param timeout the timeout in milliseconds
         */
        public bool close(long timeout) {
            try {
                if (queueThread != null) {
                    queueThread.Interrupt();
                    queueThread = null;
                }
            }
            finally {

                var staleCMDQueue = Interlocked.Exchange(ref cmdQueue, new ConcurrentQueue<Command<T>>());
                foreach (var cmd in staleCMDQueue) {
                    try {
                        cmd.requestCancellation();
                    }
                    catch (Exception ex) {
                        logger.Error(ex, "Command cancellation error");
                    }
                }
                staleCMDQueue = null;

                foreach (var cmd in replyQueue.Keys) {
                    try {
                        cmd.requestCancellation();
                        cmd.getReply().SetException(
                            new Exception("Cancellation requested. Controller shutdown.")
                        );
                    }
                    catch (Exception ex) {
                        logger.Error(ex, "Command cancellation error");
                    }

                }
                replyQueue.Clear();


                try {
                    if (cmdExecutor == null) return true;
                    try {
                        if (timeout == 0) {
                            cmdExecutor.shutdownNow();
                            return true;
                        }
                        else {
                            return cmdExecutor.awaitTermination(timeout, TimeUnit.MILLISECONDS);
                        }
                    }
                    finally {
                        cmdExecutor = null;
                    }
                }
                finally {
                    if (executor == null) return true;
                    try {
                        if (timeout == 0) {
                            executor.shutdownNow();
                            return true;
                        }
                        else {
                            return executor.awaitTermination(timeout, TimeUnit.MILLISECONDS);
                        }
                    }
                    finally {
                        executor = null;
                    }
                }
            }
        }


        /**
         * Sends a command to the device and waits for a reply (blocking).
         *
         * @param cmd command to send
         */
        public Command<T> sendCommand(Command<T> cmd) {
            try {
                sendCommandAsync(cmd).getReply().Task.Wait();
                return cmd;
            }
            catch (ThreadInterruptedException e) {
                Thread.CurrentThread.Interrupt();
                var ex = new Exception("Reply cannot be received", e);
                throw ex;
            }
            catch (AggregateException e) {
                var ex = new Exception("Reply cannot be received", e);
                throw ex;
            }
        }

        /**
         * Sends a command to the device and waits for a reply (blocking).
         *
         * @param cmd command to send
         */
        public Command<T> sendCommandAsync(Command<T> cmd) {
            dispatchCmd(cmd);
            return cmd;
        }

        /**
         * Sends the specified data to the device asynchronously.
         * @param msg the message to send
         * @return a future that will be completed when the reply message has been received
         */
        public Command<T> sendCommandAsync(T msg) {
            var command = new Command<T>(msg, null, null, null, (m, e) => {
                string eMsg = "Cannot send command: " + m;
                logger.Error(e, eMsg);
                throw new Exception(eMsg, e);
            }, null);
            sendCommandAsync(command);
            return command;
        }

        /**
         * Sends the specified data to the device (blocking).
         * @param msg the message to send
         * @return the reply message
         */
        public T sendCommand(T msg) {
            try {
                if (cmdTimeout > 0) {
                    return sendCommandAsync(msg).getReply().Task.WaitAsync(TimeSpan.FromMilliseconds(cmdTimeout)).Result;
                }
                else {
                    return sendCommandAsync(msg).getReply().Task.Result;
                }
            }
            catch (Exception e) when (e is ThreadInterruptedException || e is AggregateException || e is TimeoutException) {
                var ex = new Exception("Reply cannot be received", e);
                logger.Error(e);
                throw ex;
            }
        }

        /**
         * Sends the specified data to the device (no reply expected).
         *
         * @param msg the message to send
         * @return a future that will be completed when the data has been sent
         */
        public Task<T> sendDataAsync(T msg) {
            TaskCompletionSource<T> sentF = new TaskCompletionSource<T>();
            sendCommand(new Command<T>(msg, (m) => {
                sentF.SetResult(m);
            }, false,
                null /*no reply expected*/, (m, e) => {
                    sentF.SetException(e);
                }, null));
            return sentF.Task;
        }

        /**
         * Sends the specified data to the device (no reply expected). This method blocks until the data has been sent.
         *
         * @param msg the message to send
         */
        public void sendData(T msg) {
            TaskCompletionSource<T> sentF = new TaskCompletionSource<T>();
            sendCommand(new Command<T>(msg,
                (m) => { sentF.SetResult(m); },
                false,
                null /*no reply expected*/,
                (m, e) => { sentF.SetException(e); },
                null));

            try {
                if (dataTimeout > 0) {
                    sentF.Task.Wait(TimeSpan.FromMilliseconds(dataTimeout));
                }
                else {
                    sentF.Task.Wait();
                }
            }
            catch (Exception e) when (e is ThreadInterruptedException || e is AggregateException || e is TimeoutException) {
                var ex = new Exception("Data cannot be sent", e);
                throw ex;
            }
        }

        /**
         * Dispatches a command to the command queue.
         * @param cmd the command to dispatch
         */
        private void dispatchCmd(Command<T> cmd) {

            if (queueThread == null) {
                throw new Exception("Not initialized. Please call 'init(...)' first.");
            }

            if (cmd.isConsumed()) {
                throw new Exception("Command already consumed. Please call 'reset()' first or use a fresh command instance.");
            }

            cmdQueue.Enqueue(cmd);

            lock (simpleLock) {
                Monitor.Pulse(simpleLock);
            }
        }

    }
}
