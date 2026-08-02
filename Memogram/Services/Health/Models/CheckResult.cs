using System;
using System.Collections.Generic;
using System.Text;

namespace Memogram.Services.Health.Models
{
    public class CheckResult
    {
        public bool IsHealthy { get; init; }
        public IEnumerable<KeyValuePair<string, string>> Checks { get; init; } = null!;
    }
}
