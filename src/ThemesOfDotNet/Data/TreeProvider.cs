using System.Threading;
using System.Threading.Tasks;

namespace ThemesOfDotNet.Data
{
    public abstract class TreeProvider
    {
        public string Name => GetType().Name.Replace("TreeProvider", "");

        public abstract Task<Tree> GetTreeAsync(CancellationToken cancellationToken);
    }
}