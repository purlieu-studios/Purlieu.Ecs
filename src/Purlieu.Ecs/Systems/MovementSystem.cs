using PurlieuEcs.Components;
using PurlieuEcs.Core;
using PurlieuEcs.Events;

namespace PurlieuEcs.Systems;

/// <summary>
/// System that processes movement intents and updates positions.
/// Implements Backend-Visual Intent Pattern (BVIP).
/// </summary>
[GamePhase(GamePhases.Update, order: 100)]
public sealed class MovementSystem : ISystem
{
    public void Update(World world, float deltaTime)
    {
        var query = world.Query()
            .With<Position>()
            .With<MoveIntent>()
            .Without<Stunned>();
        
        var eventChannel = world.Events<PositionChangedIntent>();
        
        foreach (var chunk in query.Chunks())
        {
            var positions = chunk.GetSpan<Position>();
            var moveIntents = chunk.GetSpan<MoveIntent>();
            
            for (int i = 0; i < chunk.Count; i++)
            {
                ref var pos = ref positions[i];
                ref readonly var intent = ref moveIntents[i];
                
                int oldX = pos.X;
                int oldY = pos.Y;
                
                pos.X += intent.DX;
                pos.Y += intent.DY;
                
                if (pos.X != oldX || pos.Y != oldY)
                {
                    var entity = chunk.GetEntity(i);
                    eventChannel.Publish(new PositionChangedIntent(entity, pos.X, pos.Y));
                }
            }
        }
    }
}