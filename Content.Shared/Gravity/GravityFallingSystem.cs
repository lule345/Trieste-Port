using System.Linq;
using Content.Shared.ActionBlocker;
using Content.Shared.Buckle.Components;
using Content.Shared.Movement.Events;
using Content.Shared.StepTrigger.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;
using Robust.Shared.Map;

namespace Content.Shared.GravityFalling
{
    public sealed class WeightlessFallingSystem : EntitySystem
    {
        [Dependency] private readonly IGameTiming _timing = default!;
        [Dependency] private readonly ActionBlockerSystem _blocker = default!;
        [Dependency] private readonly INetManager _net = default!;
        [Dependency] private readonly SharedAudioSystem _audio = default!;
        [Dependency] private readonly SharedGravitySystem _gravitySystem = default!;
        [Dependency] private readonly SharedTransformSystem _transform = default!;
        
        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<GravityComponent, UpdateCanMoveEvent>(OnUpdateCanMove);
            SubscribeLocalEvent<GravityChangedEvent>(OnGravityChange);
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            if (_net.IsClient)
                return;

            var query = EntityQueryEnumerator<ChasmFallingComponent>();
            while (query.MoveNext(out var uid, out var chasm))
            {
                if (_timing.CurTime < chasm.NextDeletionTime)
                    continue;

                QueueDel(uid); // This can be modified to teleport instead of delete
            }
        }

        private void OnUpdateCanMove(EntityUid uid, GravityComponent component, UpdateCanMoveEvent args)
        {
            CheckAndStartFalling(uid);
        }

        private void OnGravityChange(ref GravityChangedEvent ev)
        {
            CheckAndStartFalling(ev.ChangedGridIndex);
        }

        private void CheckAndStartFalling(EntityUid uid)
        {
            if (_gravitySystem.IsWeightless(uid))
            {
                StartFalling(uid);
            }
        }

        private void StartFalling(EntityUid entity, bool playSound = true)
        {
            var falling = AddComp<ChasmFallingComponent>(entity);
            falling.NextDeletionTime = _timing.CurTime + falling.DeletionTime;

            _blocker.UpdateCanMove(entity);

            if (playSound)
                _audio.PlayPredicted(entity);

            // Modify to teleport instead of delete
            TeleportToFallDestination(entity);
        }

        private void TeleportToFallDestination(EntityUid entity)
        {
            // Find the nearest FallDestination entity
            var fallDestinations = EntityManager.EntityQuery<FallDestination>().ToList();
            if (fallDestinations.Any())
            {
                var fallDestination = fallDestinations.First();
                var destinationCoords = Transform(fallDestination.Owner).Coordinates;
                _transform.SetCoordinates(entity, destinationCoords);

                // Play sound or other effects if needed
                _audio.PlayPredicted(fallDestinationSound, entity);
            }
        }
    }
}
