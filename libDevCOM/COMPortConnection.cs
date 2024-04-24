using libDevCOM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libDevCOM {
    public sealed class COMPortConnection<T> : DataConnection<T, COMPortConnection<T>> {
        public void Dispose() {
            throw new NotImplementedException();
        }

        public COMPortConnection<T> setOnDataReceived(Action<T> onDataReceived) {
            throw new NotImplementedException();
        }

        public Unsubscribe registerConnectionOpenedListener(Action<COMPortConnection<T>> l) {
            throw new NotImplementedException();
        }

        public COMPortConnection<T> setOnIOError(Action<COMPortConnection<T>, Exception> onIOError) {
            throw new NotImplementedException();
        }

        public Unsubscribe registerDataListener(Action<T> l) {
            throw new NotImplementedException();
        }

        public Unsubscribe registerConnectionClosedListener(Action<COMPortConnection<T>> l) {
            throw new NotImplementedException();
        }

        public Unsubscribe registerIOErrorListener(Action<COMPortConnection<T>, Exception> l) {
            throw new NotImplementedException();
        }

        public void writeData(T msg) {
            throw new NotImplementedException();
        }

        public DataFormat<T> getFormat() {
            throw new NotImplementedException();
        }

        public bool isOpen() {
            throw new NotImplementedException();
        }

        public void open() {
            throw new NotImplementedException();
        }

        public void close() {
            throw new NotImplementedException();
        }

        public void setOnConnectionClosed(Action<COMPortConnection<T>> onConnectionClosed) {
            throw new NotImplementedException();
        }
    }
}
