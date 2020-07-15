using System.Collections.Generic;

namespace FunctionCustomHandlers
{
    public class InvocationResponse
    {
        public object ReturnValue { get; set; }
        public IDictionary<string, object> Outputs { get; set; } = new Dictionary<string, object>();

        public List<string> Logs { get; set; } = new List<string>();
    }
}