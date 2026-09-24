using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JortPob.Logging
{
    internal interface ILogOutput
    {
        // Log lines should be submitted finalized, there is no additional processing done by the output
        void SubmitLog(string message);
    }
}
