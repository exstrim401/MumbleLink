using MumbleLinkPlugin;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace MumbleLink
{
    public class MumbleLinkSystem : ModSystem
    {
        private ICoreClientAPI capi;
        private long gameTickListenerId;
        private Process consumerProcess;
        private AnonymousPipeServerStream pipeServer;
        private bool skipConsumer = false;
        private PositionInfoSupplier supplier;
        private readonly PositionInfo positionInfo = new PositionInfo();

        public override bool ShouldLoad(EnumAppSide forSide)
        {
            return forSide == EnumAppSide.Client;
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            LaunchPositionConsumer();
            gameTickListenerId = capi.Event.RegisterGameTickListener(OnGameTick, 20);
        }

        private void LaunchPositionConsumer()
        {
            var currentDir = Path.GetDirectoryName(GetType().Assembly.Location);
            var consumerDll = Path.Combine(currentDir, $"{nameof(MumbleLinkPlugin)}.dll");

            pipeServer = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
            consumerProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"{consumerDll} {PositionInfo.Size} {pipeServer.GetClientHandleAsString()}",
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                }
            };

            bool success = consumerProcess.Start();
            if (!success)
            {
                capi.Logger.Error("[MumbleLink] Failed to start positon info consumer process");
                skipConsumer = true;
            }

            pipeServer.DisposeLocalCopyOfClientHandle();
        }

        private void OnGameTick(float delta)
        {
            if (capi.World?.Player?.Entity == null) return;
            if (consumerProcess.HasExited && !skipConsumer)
            {
                capi.Logger.Warning("[MumbleLink] Consumer process has exited. Picking up its role(will be affected by lag)");
                skipConsumer = true;
            }

            UpdatePosition();
            SupplyPosition();
        }

        private void UpdatePosition()
        {
            positionInfo.Context = capi.World.Seed.ToString();
            positionInfo.Identity = capi.World.Player.PlayerUID;

            var entity = capi.World.Player.Entity;

            // Mumble Link uses left-handed coordinate system (+X is to the right)
            // wheras Vintage Story uses a right-handed one (where +X is to the left),
            // so we actually have the flip the X coordinate to get the right values.
            static Vec3d FlipX(Vec3d vec) => new(-vec.X, vec.Y, vec.Z);

            var headPitch = entity.Pos.HeadPitch;
            var headYaw = entity.Pos.Yaw + entity.Pos.HeadYaw;
            positionInfo.AvatarPosition = FlipX(entity.Pos.XYZ + entity.LocalEyePos);
            positionInfo.AvatarFront = new Vec3d(
                -GameMath.Cos(headYaw) * GameMath.Cos(headPitch),
                -GameMath.Sin(headPitch),
                -GameMath.Sin(headYaw) * GameMath.Cos(headPitch));

            positionInfo.CameraPosition = FlipX(entity.CameraPos);
            positionInfo.CameraFront = positionInfo.AvatarFront;
        }

        private void SupplyPosition()
        {
            var data = positionInfo.ToBytes();

            if (skipConsumer)
            {
                supplier ??= CreateSupplier();
                supplier.OnNewMessage(data);
                return;
            }

            try
            {
                pipeServer.Write(data, 0, data.Length);
                if (OperatingSystem.IsWindows())
                    pipeServer.WaitForPipeDrain();
            }
            catch (Exception e)
            {
                capi.Logger.Error($"[MumbleLink] Failed to send position info to consumer:\n{e}");
                pipeServer?.Dispose();
            }
        }

        private PositionInfoSupplier CreateSupplier()
        {
            var supplier = new PositionInfoSupplier(PositionInfo.Size);
            supplier.Init();
            supplier.Start();
            return supplier;
        }

        public override void Dispose()
        {
            capi?.Event.UnregisterGameTickListener(gameTickListenerId);
            pipeServer?.Dispose();
            consumerProcess?.Close();
            consumerProcess?.Dispose();
            supplier?.Dispose();
        }
    }
}
