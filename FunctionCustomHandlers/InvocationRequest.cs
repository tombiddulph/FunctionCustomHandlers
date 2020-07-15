using System.Collections.Generic;

namespace FunctionCustomHandlers
{
    public class InvocationRequest
    {
        public IDictionary<string, object> Data { get; set; } = new Dictionary<string, object>();

        public IDictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }
}