using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ControllerPlayground.KeyboardHost.Models {
    internal sealed record KeyboardKeyDefinition(
        string Primary,
        string? Secondary = null,
        string? Action = null,
        double Width = 76);
}
