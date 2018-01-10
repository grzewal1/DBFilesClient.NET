﻿using DBFilesClient2.NET.Exceptions;
using DBFilesClient2.NET.Internals;
using System;
using System.Linq;

namespace DBFilesClient2.NET.Implementations.WDB2
{
    internal class WDB2Reader<TKey, TValue> : BaseStorageReader<TKey, TValue, WDB2Header>
        where TValue : class, new()
        where TKey : struct
    {
        public WDB2Reader(StorageOptions options) : base(options)
        {
        }

        public override bool ParseHeader(BinaryReader reader)
        {
            _header.RecordCount = reader.ReadInt32();
            if (_header.RecordCount == 0)
                return false;

            _header.FieldCount = reader.ReadInt32();
            _header.RecordSize = reader.ReadInt32();

            var stringTableSize = reader.ReadInt32();

            reader.BaseStream.Position += 4 + 4 + 4; // table hash, Build and timestamp
            _header.MinIndex = reader.ReadInt32();
            _header.MaxIndex = reader.ReadInt32();
            reader.BaseStream.Position += 4 + 4; // Locales, copy table size

            TypeMembers = new FieldMetadata[Members.Length];
            for (var i = 0; i < Members.Length; ++i)
            {
                TypeMembers[i] = new FieldMetadata();
                TypeMembers[i].ByteSize = SizeCache.GetSizeOf(Members[i].GetMemberType());
                TypeMembers[i].MemberInfo = Members[i];

                if (i > 0)
                    TypeMembers[i].OffsetInRecord = (uint)(TypeMembers[i - 1].OffsetInRecord + TypeMembers[i - 1].ByteSize * TypeMembers[i - 1].GetArraySize());
                else
                    TypeMembers[i].OffsetInRecord = 0;
            }

            if (TypeMembers.Sum(t => t.GetArraySize()) != _header.FieldCount)
                throw new InvalidStructureException<TValue>(ExceptionReason.StructureSizeMismatch, Header.RecordSize, Serializer.Size);

            // Check size matches
            if (_header.RecordSize != Serializer.Size)
               throw new InvalidStructureException<TValue>(ExceptionReason.StructureSizeMismatch, _header.RecordSize, Serializer.Size);

            _header.OffsetMap.Exists = _header.MaxIndex != 0;
            _header.OffsetMap.StartOffset = reader.BaseStream.Position;
            _header.OffsetMap.Size = (_header.MaxIndex - _header.MinIndex + 1) * (4 + 2);

            _header.RecordTable.Exists = true; 
            _header.RecordTable.StartOffset = _header.OffsetMap.EndOffset;
            _header.RecordTable.Size = _header.RecordCount * _header.RecordSize;

            _header.StringTable.Exists = true;
            _header.StringTable.Size = stringTableSize;
            _header.StringTable.StartOffset = _header.RecordTable.EndOffset;
            return true;
        }

        protected override void LoadRecords(BinaryReader reader)
        {
            if (!Options.LoadRecords)
                return;

            reader.BaseStream.Position = _header.RecordTable.StartOffset;

            for (var i = 0; i < _header.RecordCount; ++i)
            {
                var recordOffset = reader.BaseStream.Position;

                var newRecord = Serializer.Deserializer(reader);
                var newKey = Serializer.GetKey(newRecord);

                // Store the offset to the record and skip to the next, thus making sure
                // to take record padding into consideration.
                OffsetMap[newKey] = recordOffset;

#if DEBUG
                if (reader.BaseStream.Position > recordOffset + _header.RecordSize)
                    throw new InvalidOperationException();
#endif

                reader.BaseStream.Position = recordOffset + _header.RecordSize;

                OnRecordLoaded(newKey, newRecord);
            }
        }
    }
}
