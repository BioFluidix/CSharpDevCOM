using libDevCOM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libDevCOM {

    public interface DataConnection<T, V> : IDisposable
        where V : DataConnection<T, V> {

        V setOnDataReceived(Action<T> onDataReceived);

        V setOnIOError(Action<V, Exception> onIOError);

        Unsubscribe registerDataListener(Action<T> l);

        Unsubscribe registerConnectionOpenedListener(Action<V> l);

        Unsubscribe registerConnectionClosedListener(Action<V> l);

        Unsubscribe registerIOErrorListener(Action<V, Exception> l);

        void writeData(T msg);

        DataFormat<T> getFormat();

        bool isOpen();

        void open();

        void close();

        void setOnConnectionClosed(Action<V> onConnectionClosed);

    }
}
