using BenchmarkDotNet.Running;

namespace Purlieu.Ecs.Benchmarks;

class Program
{
    static void Main(string[] args)
    {
        BenchmarkRunner.Run<BENCH_EntityCreation>();
    }
}
