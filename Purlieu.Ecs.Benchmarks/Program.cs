using BenchmarkDotNet.Running;

namespace Purlieu.Ecs.Benchmarks;

class Program
{
    static void Main(string[] args)
    {
        // Quick validation mode
        if (args.Length > 0 && args[0] == "--quick")
        {
            QuickValidation.Run();
            return;
        }
        
        // Full benchmark mode
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
