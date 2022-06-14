using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace RIO
{
    /// <summary>
    /// Base class to offer the <see cref="INotifyPropertyChanged"/> interface.
    /// </summary>
    public class DataModel : INotifyPropertyChanged
    {
        /// <summary>
        ///    Occurs when a property value changes. It provides a <see cref="PropertyChangedEventArgs"/>
        ///    to describe the property change.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;
        /// <summary>
        /// Sets the new value of the property and raises the <see cref="PropertyChanged"/> event, if different from
        /// the present value.
        /// </summary>
        /// <typeparam name="T">The type of the property.</typeparam>
        /// <param name="storage">A field to store the value of the property.</param>
        /// <param name="value">The value to be set.</param>
        /// <param name="propertyName">The name of the property, when not desumed by the calling method.</param>
        protected void Set<T>(ref T storage, T value, [CallerMemberName]string propertyName = null)
        {
            if (Equals(storage, value))
            {
                return;
            }

            storage = value;
            OnPropertyChanged(propertyName);
        }
        /// <summary>
        /// Use this method to raise the <see cref="PropertyChanged"/> event.
        /// </summary>
        /// <param name="propertyName">The name of the property that changed its value.</param>
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
