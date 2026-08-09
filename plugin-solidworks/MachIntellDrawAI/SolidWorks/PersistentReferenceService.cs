using System;
using MachIntellDrawAI.Infrastructure;
using MachIntellDrawAI.Models;
using SolidWorks.Interop.sldworks;

namespace MachIntellDrawAI.SolidWorks
{
    internal sealed class PersistentReferenceService
    {
        private readonly IModelDoc2 _model;
        private readonly string _configuration;

        public PersistentReferenceService(IModelDoc2 model, string configuration)
        {
            _model = model;
            _configuration = configuration;
        }

        public EntityRef Create(object entity, string entityType)
        {
            var bytes = (byte[]?)_model.Extension.GetPersistReference3(entity)
                ?? throw new InvalidOperationException("SolidWorks returned no persistent reference for " + entityType);
            if (bytes.Length == 0)
                throw new InvalidOperationException("SolidWorks returned an empty persistent reference for " + entityType);
            return new EntityRef
            {
                Token = Convert.ToBase64String(bytes),
                EntityType = entityType,
                ModelConfiguration = _configuration
            };
        }

        public object Resolve(EntityRef entityRef)
        {
            if (!string.Equals(entityRef.ModelConfiguration, _configuration, StringComparison.Ordinal))
                throw new InvalidOperationException($"Persistent reference configuration mismatch: {entityRef.ModelConfiguration} != {_configuration}");
            int status;
            var resolved = _model.Extension.GetObjectByPersistReference3(Convert.FromBase64String(entityRef.Token), out status);
            if (resolved == null || status != 0)
                throw new InvalidOperationException($"Persistent reference could not be resolved ({entityRef.EntityType}, status {status}).");
            return resolved;
        }

        public string StableId(EntityRef entityRef, string prefix) =>
            StableHash.Bytes(Convert.FromBase64String(entityRef.Token), prefix);
    }
}
