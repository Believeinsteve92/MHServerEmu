﻿using MHServerEmu.GameServer.Common;

namespace MHServerEmu.GameServer.Regions
{
    public class Cell
    {
        public uint Id { get; }
        public ulong PrototypeId { get; }
        public Vector3 PositionInArea { get; }
        //TODO: encounters

        public Cell(uint id, ulong prototypeId, Vector3 positionInArea)
        {
            Id = id;
            PrototypeId = prototypeId;
            PositionInArea = positionInArea;
        }
    }
}
