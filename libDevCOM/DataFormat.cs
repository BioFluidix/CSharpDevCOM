using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libDevCOM {
    /// <summary>
    /// Data format for device communication.
    /// </summary>
    /// <typeparam name="T">data type (e.g. String or binary packet)</typeparam>
    public interface DataFormat<T> {
        /// <summary>
        /// Reads data from the specified input stream.
        /// </summary>
        /// <param name="inputStream">input stream for reading the data</param>
        /// <returns>data that has been read from the specified input stream</returns>
        /// <exception cref="IOException">if an I/O error occurs</exception>
        T readData(Stream inputStream);

        /// <summary>
        /// Writes data to the specified input stream.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="outputStream">output stream for writing the data</param>
        /// <exception cref="IOException">if an I/O error occurs</exception>
        void writeData(T data, Stream outputStream);

        /// <summary>
        /// Determines whether the specified data is a reply to the command.
        /// </summary>
        /// <param name="cmd">the command</param>
        /// <param name="replyData">potential reply packet</param>
        /// <returns>{@code true} if the data is a reply to the command; {@code false otherwise}</returns>
        bool isReply(Command<T> cmd, T replyData);

        /// <summary>
        /// Returns a value contained in the data by name, e.g., a packet entry.
        /// </summary>
        /// 
        /// <param name="name">name of the value to access</param>
        /// <param name="data">data to access</param>
        /// <typeparam name="V">data type of the value, e.g., Integer or Boolean.</typeparam>
        /// <returns>the optional value by name (value might not exits)</returns>
        public V? getValueByName<V>(string name, V data) {
            return default(V);
        }
    }
}
