﻿//
// Copyright (c) 2015 Timotei Dolean
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//       http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
using Confuser.Renamer.BAML;
using Fasterflect;
using Mono.Cecil;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Resources;

namespace ILRepacking.Steps.ResourceProcessing
{
    /// <summary>
    /// This collector removes the BAML streams from non-main assemblies and
    /// moves them in a assembly-name directory, to prevent duplication.
    /// </summary>
    internal class BamlStreamCollector : IResProcessor, IEmbeddedResourceProcessor
    {
        private const string GenericThemesBamlName = "themes/generic.baml";

        private readonly AssemblyDefinition _primaryAssemblyDefinition;
        private readonly BamlGenerator _bamlGenerator;
        private readonly List<string> _genericThemeResources = new List<string>();

        public Dictionary<Res, AssemblyDefinition> BamlStreams { get; private set; }

        public BamlStreamCollector(IRepackContext repackContext)
        {
            _primaryAssemblyDefinition = repackContext.PrimaryAssemblyDefinition;

            BamlStreams = new Dictionary<Res, AssemblyDefinition>();
            _bamlGenerator = new BamlGenerator(
                repackContext.TargetAssemblyMainModule.AssemblyReferences, _primaryAssemblyDefinition);
        }

        public bool Process(AssemblyDefinition containingAssembly,
            Res resource, ResReader resourceReader, ResourceWriter resourceWriter)
        {
            if (resource.IsBamlStream)
            {
                if (resource.name.EndsWith(GenericThemesBamlName, StringComparison.OrdinalIgnoreCase))
                {
                    _genericThemeResources.Add(GetResourceName(resource, containingAssembly));
                }

                BamlStreams.Add(resource, containingAssembly);
                return true;
            }
            return false;
        }

        public void Process(EmbeddedResource embeddedResource, ResourceWriter resourceWriter)
        {
            WriteCollectedBamlStreams(resourceWriter);
            PatchGenericThemesBaml(resourceWriter);
        }

        private void WriteCollectedBamlStreams(ResourceWriter resourceWriter)
        {
            foreach (var bamlStream in BamlStreams)
            {
                resourceWriter.AddResourceData(
                    GetResourceName(bamlStream.Key, bamlStream.Value), bamlStream.Key.type, bamlStream.Key.data);
            }
        }

        private void PatchGenericThemesBaml(ResourceWriter resourceWriter)
        {
            byte[] existingGenericBaml;
            if (!TryGetPreserializedData(resourceWriter, GenericThemesBamlName, out existingGenericBaml))
            {
                BamlDocument generatedDocument = _bamlGenerator.GenerateThemesGenericXaml(_genericThemeResources);
                using (var stream = new MemoryStream())
                {
                    BamlWriter.WriteDocument(generatedDocument, stream);

                    resourceWriter.AddResourceData(
                        GenericThemesBamlName, "ResourceTypeCode.Stream", BitConverter.GetBytes((int)stream.Length).Concat(stream.ToArray()).ToArray());
                }
            }
        }

        private string GetResourceName(Res resource, AssemblyDefinition assembly)
        {
            if (assembly == _primaryAssemblyDefinition)
                return resource.name;

            return string.Format("{0}/{1}", assembly.Name.Name.ToLowerInvariant(), resource.name);
        }

        private static bool TryGetPreserializedData(ResourceWriter resourceWriter, string resourceName, out byte[] preserializedData)
        {
            IDictionary resourcesHashtable = (IDictionary)resourceWriter.GetFieldValue("_preserializedData");
            if (resourcesHashtable == null || !resourcesHashtable.Contains(resourceName))
            {
                preserializedData = null;
                return false;
            }

            object precannedResource = resourcesHashtable[resourceName];
            preserializedData = precannedResource.GetFieldValue("Data") as byte[];

            return true;
        }
    }
}
