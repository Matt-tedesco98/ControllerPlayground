using System;
using System.Collections.Generic;

namespace ControllerPlayground.Services {
    internal sealed class VdfNode {

        public string? Value { get; set; }

        public Dictionary<string, VdfNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool TryGetChild(string key, out VdfNode? child) {
            return Children.TryGetValue(key, out child);
        }
    }
}
