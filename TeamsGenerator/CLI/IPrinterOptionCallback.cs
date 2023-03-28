using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TeamsGenerator.CLI
{
    internal interface IPrinterOptionCallback
    {
        void DoCommand();

        string CommandDescription { get; }
    }
}
