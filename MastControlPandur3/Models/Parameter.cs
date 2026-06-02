using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;

namespace MastControlPandur3
{
    internal class Parameter : INotifyPropertyChanged
    {
        private UInt16 _wert;
        private UInt16? _neuerWert;

        public Parameter(string typ, bool useMastParamsV, byte idx1, byte idx2, string name, string einheit)
        {
            Name = name;
            Typ = typ;
            Einheit = einheit;
            UseMastParamsV = useMastParamsV;
            Idx1 = idx1;
            Idx2 = idx2;
        }

        public string Name { get; private set; }
        public string Typ { get; private set; }
        public string Einheit { get; private set; }
        public bool UseMastParamsV { get; private set; }
        public byte Idx1 { get; private set; }
        public byte Idx2 { get; private set; }
        public byte HPN => (byte)((UseMastParamsV ? 0x80 : 0x00)
                    + (Idx1 << 4)
                    + Idx2);

        public UInt16 Wert { get => _wert; set { if (_wert != value) { _wert = value; NotifyOfPropertyChange(); } } }
        public UInt16? NeuerWert { get => _neuerWert; set { if (_neuerWert != value) { _neuerWert = value; NotifyOfPropertyChange(); } } }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void NotifyOfPropertyChange([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
