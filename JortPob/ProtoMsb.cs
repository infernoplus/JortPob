using System.Collections.Generic;
using System.Numerics;
using JortPob.Common;

namespace JortPob
{
    // IMSBCompilableGroup is implemented by InteriorGroup and Tiles to improve code reuse when generating MSBs.
    public interface IMSBCompilableGroup
    {
        int map { get; }
        Int2 coordinate { get; }
        int block { get; }
        int[] IdList();

        bool IsEmpty { get; }
        bool IsInterior { get; }

        List<IMSBCompilableChunk> Chunks { get; }  // Tiles will return a List containing itself only.
    }

    // IMSBCompilableChunk is implemented by Chunks within an InteriorGroup.
    // It is also implemented by Tiles, though they don't contain chunks so each Tile is treated as a single chunk.
    public interface IMSBCompilableChunk
    {
        Vector3 root { get; }
        Vector3 bounds { get; }
        List<Cell> cells { get; }  // Each Chunk in an InteriorGroup will only return 1 cell.

        bool IsInterior { get; }

        List<AssetContent> assets { get; }
        List<DoorContent> doors { get; }
        List<LightContent> lights { get; }
        List<EmitterContent> emitters { get; }
        List<CreatureContent> creatures { get; }
        List<NpcContent> npcs { get; }
        List<ContainerContent> containers { get; }
        List<PickableContent> pickables { get; }
        List<ItemContent> items { get; }
        List<Layout.WarpDestination> warps { get; } // end points for load doors in other cells. also used by travel npcs
        List<Layout.ScriptedPosition> positions { get; } // used by scripts to target locations EX: 'PositionCell'
        List<Layout.TravelPoint> travels { get; } // positions directly referenced in AiPackages
        List<Layout.PathGridPoint> paths { get; }
        List<Layout.MapPoint> points { get; }  // Each Chunk in an InteriorGroup will only return 1 MapPoint.
    }
}