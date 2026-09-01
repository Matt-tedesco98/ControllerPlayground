using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ControllerPlayground.Services {
    public interface ISteamSessionService {

        bool IsConnected { get; }
        bool IsAuthenticated { get; }

        void Connect();
        void Disconnect();
    }
}
