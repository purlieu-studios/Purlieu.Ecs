using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using PurlieuEcs.Core;
using PurlieuEcs.Events;

namespace Purlieu.Ecs.Benchmarks;

[Config(typeof(Config))]
[MemoryDiagnoser]
public class BENCH_EventPublishing
{
    private class Config : ManualConfig
    {
        public Config()
        {
            AddJob(Job.Default.WithWarmupCount(3).WithIterationCount(10));
        }
    }

    public struct TestEvent
    {
        public int EntityId;
        public float Value;
        public long Timestamp;
    }

    public struct LargeEvent
    {
        public int Id1, Id2, Id3, Id4;
        public float X, Y, Z, W;
        public double Timestamp;
        public bool Flag1, Flag2, Flag3;
        public byte Data1, Data2, Data3, Data4;
    }

    private World _world = null!;
    private EventChannel<TestEvent> _eventChannel = null!;
    private EventChannel<LargeEvent> _largeEventChannel = null!;
    private List<TestEvent> _eventList = null!;

    [Params(1000, 10_000, 100_000)]
    public int EventCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _world = new World();
        _eventChannel = new EventChannel<TestEvent>(EventCount * 2);
        _largeEventChannel = new EventChannel<LargeEvent>(EventCount * 2);
        _eventList = new List<TestEvent>(EventCount);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _world = null!;
        _eventChannel = null!;
        _largeEventChannel = null!;
        _eventList = null!;
    }

    [Benchmark(Baseline = true)]
    public void ListPublishConsume()
    {
        _eventList.Clear();
        
        // Publish events
        for (int i = 0; i < EventCount; i++)
        {
            _eventList.Add(new TestEvent { EntityId = i, Value = i * 0.1f, Timestamp = i });
        }
        
        // Consume events
        long sum = 0;
        foreach (var evt in _eventList)
        {
            sum += evt.EntityId;
        }
        _eventList.Clear();
    }

    [Benchmark]
    public void EventChannelPublishConsume()
    {
        _eventChannel.Clear();
        
        // Publish events
        for (int i = 0; i < EventCount; i++)
        {
            _eventChannel.Publish(new TestEvent { EntityId = i, Value = i * 0.1f, Timestamp = i });
        }
        
        // Consume events
        long sum = 0;
        _eventChannel.ConsumeAll(evt => sum += evt.EntityId);
    }

    [Benchmark]
    public void EventChannelPublishConsumeStruct()
    {
        _eventChannel.Clear();
        
        // Publish events
        for (int i = 0; i < EventCount; i++)
        {
            _eventChannel.Publish(new TestEvent { EntityId = i, Value = i * 0.1f, Timestamp = i });
        }
        
        // Consume events with struct state (zero allocation)
        var state = new ProcessingState { Sum = 0, Count = 0 };
        _eventChannel.ConsumeAll(state, (in TestEvent evt, ref ProcessingState s) =>
        {
            s.Sum += evt.EntityId;
            s.Count++;
        });
    }

    [Benchmark]
    public void LargeEventPublishConsume()
    {
        _largeEventChannel.Clear();
        
        // Publish larger events
        for (int i = 0; i < EventCount; i++)
        {
            _largeEventChannel.Publish(new LargeEvent 
            { 
                Id1 = i, Id2 = i + 1, Id3 = i + 2, Id4 = i + 3,
                X = i, Y = i, Z = i, W = i,
                Timestamp = i,
                Flag1 = i % 2 == 0, Flag2 = i % 3 == 0, Flag3 = i % 5 == 0,
                Data1 = (byte)i, Data2 = (byte)(i + 1), Data3 = (byte)(i + 2), Data4 = (byte)(i + 3)
            });
        }
        
        // Consume events
        long sum = 0;
        _largeEventChannel.ConsumeAll(evt => sum += evt.Id1 + evt.Id2 + evt.Id3 + evt.Id4);
    }

    [Benchmark]
    public void WorldEventChannelAccess()
    {
        // Test the overhead of accessing events through World
        var events = _world.Events<TestEvent>();
        events.Clear();
        
        // Publish events
        for (int i = 0; i < EventCount; i++)
        {
            events.Publish(new TestEvent { EntityId = i, Value = i * 0.1f, Timestamp = i });
        }
        
        // Consume events
        long sum = 0;
        events.ConsumeAll(evt => sum += evt.EntityId);
    }

    [Benchmark]
    public void EventChannelOverflowBehavior()
    {
        // Test ring buffer overflow behavior
        var smallChannel = new EventChannel<TestEvent>(100);
        
        // Publish more events than capacity
        for (int i = 0; i < EventCount; i++)
        {
            smallChannel.Publish(new TestEvent { EntityId = i, Value = i * 0.1f, Timestamp = i });
        }
        
        // Consume available events
        long sum = 0;
        smallChannel.ConsumeAll(evt => sum += evt.EntityId);
    }

    [Benchmark]
    public void EventPublishingThroughput()
    {
        _eventChannel.Clear();
        
        // Pure publishing throughput
        for (int i = 0; i < EventCount; i++)
        {
            _eventChannel.Publish(new TestEvent { EntityId = i, Value = i * 0.1f, Timestamp = i });
        }
    }

    [Benchmark]
    public void EventConsumingThroughput()
    {
        _eventChannel.Clear();
        
        // Pre-fill with events
        for (int i = 0; i < EventCount; i++)
        {
            _eventChannel.Publish(new TestEvent { EntityId = i, Value = i * 0.1f, Timestamp = i });
        }
        
        // Pure consuming throughput
        long sum = 0;
        _eventChannel.ConsumeAll(evt => sum += evt.EntityId + (long)evt.Value + evt.Timestamp);
    }

    private struct ProcessingState
    {
        public long Sum;
        public int Count;
    }
}