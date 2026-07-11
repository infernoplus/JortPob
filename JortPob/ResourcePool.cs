using JortPob.Scripts;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JortPob
{
    public class ResourcePool
    {
        public int[] id;
        public List<Tuple<int, string>> mapIndices;
        public MSBE msb;
        public LightManager lights;
        public BaseScript script;
        public List<Tuple<string, CollisionInfo>> collisionIndices;

        /* Interior and Exterior cells */
        public ResourcePool(IMSBCompilableGroup group, MSBE msb, LightManager lights, BaseScript script = null)
        {
            id = [group.map, group.coordinate.x, group.coordinate.y, group.block];
            mapIndices = new();
            collisionIndices = new();
            this.msb = msb;
            this.lights = lights;
            this.script = script;
        }

        /* Super overworld */
        public ResourcePool(MSBE msb, LightManager lights)
        {
            id = new int[]
            {
                    60, 00, 00, 99
            };
            mapIndices = new();
            this.msb = msb;
            this.lights = lights;
            script = null;
            collisionIndices = new();
        }

        public void Add(TerrainInfo terrain)
        {
            mapIndices.Add(new Tuple<int, string>(terrain.id, terrain.path));
        }

        public void Add(string index, CollisionInfo collision)
        {
            collisionIndices.Add(new Tuple<string, CollisionInfo>(index, collision));
        }
    }
}
