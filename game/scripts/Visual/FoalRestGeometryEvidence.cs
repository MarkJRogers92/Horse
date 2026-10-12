using System;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace Cloverbrook.Game.Visual;

/// <summary>Owner-approved corrected foal rest surface shared by Idle and Walk.
/// Original rig-only/foundation proofs stay historical; this is a bounded edit.</summary>
internal static class FoalRestGeometryEvidence
{
    internal const string Glb = "422d320d4c976411e3f6d4346d74202b606f438bade44111b325686575c86291";
    internal const string Blend = "bef2fe3a18f2d742d0a5e4976dd25d489959be381e408214bfd4323bd725fe31";
    private const string WalkGlb = "5f302d885e7a331de7cbc7f494846becac5eceae062e8ffed805ab2ff31beddc";
    private const string Root = "res://assets/horse/production/evidence/foal-idle-hind-geometry/";
    private const string RegionHash = "50f85deb4e88d0f980ea822c2f57224ce747fb2eea7ed58ba6769ae117a70248";
    private const string ConsistencyHash = "a92ec0d29757f49d744db1f0e03ca7c3046fa75389dc39f3e78b61b6bb297650";
    private const string MorphHash = "b0db735a0b20d8a4d5a6c5ba08cb48595e2b9d5be6edee6dc02b14bbc02d8051";
    private const string ImportHash = "827a0951b71246006b373f4f1e4cebdffd65f1af76424f8c98ea829c27ea9d42";

    internal static string? DisabledReason(JsonElement family, string id, string scene)
    {
        if (id is not ("horse.standard.foal.v1" or "horse.compact.foal.v1")) return null;
        const string problem = "Corrected foal Idle/Walk rest geometry evidence is missing, stale, or inconsistent";
        try
        {
            if (scene != "res://assets/horse/production/horse_foal.glb" ||
                family.GetProperty("age").GetString() != "foal" ||
                !family.TryGetProperty("restGeometry", out var binding) ||
                Text(binding,"sourceGlbSha256") != Glb || Text(binding,"candidateBlendSha256") != Blend ||
                Text(binding,"regionProofPath") != Root+"bounded-geometry-audit.json" ||
                Text(binding,"regionProofSha256") != RegionHash ||
                Text(binding,"consistencyProofPath") != Root+"idle-walk-consistency.json" ||
                Text(binding,"consistencyProofSha256") != ConsistencyHash ||
                Text(binding,"morphProofPath") != Root+"idle-morph-audit.json" ||
                Text(binding,"morphProofSha256") != MorphHash || Text(binding,"importProofPath") != Root+"native-idle-report.json" ||
                Text(binding,"importProofSha256") != ImportHash || Hash(scene) != Glb ||
                Hash("res://assets/horse/production/horse_foal_walk.glb") != WalkGlb ||
                Hash(Root+"bounded-geometry-audit.json") != RegionHash ||
                Hash(Root+"idle-walk-consistency.json") != ConsistencyHash ||
                Hash(Root+"idle-morph-audit.json") != MorphHash || Hash(Root+"native-idle-report.json") != ImportHash) return problem;
            using var regionDoc=JsonDocument.Parse(FileAccess.GetFileAsBytes(Root+"bounded-geometry-audit.json"));
            using var consistencyDoc=JsonDocument.Parse(FileAccess.GetFileAsBytes(Root+"idle-walk-consistency.json"));
            using var morphDoc=JsonDocument.Parse(FileAccess.GetFileAsBytes(Root+"idle-morph-audit.json"));
            var region=regionDoc.RootElement; var consistency=consistencyDoc.RootElement; var morph=morphDoc.RootElement;
            if (Text(region,"schema") != "horse.foal_walk.bounded_geometry.v1" ||
                Text(region,"sourceBlendSha256") != "4322de7c3b97e6aa0e71111a6ab0de325f72f4f905424b4ff6e8d22df0ca041b" ||
                Text(region,"sourceGlbSha256") != "f7552842a1ae8a24fb32df60c6d5de8200dfe19055c9787330cf1ac5ff72c72a" ||
                Text(region,"candidateBlendSha256") != Blend || Text(region,"candidateGlbSha256") != Glb ||
                !Passed(region) || region.GetProperty("allPositionsExact").ValueKind != JsonValueKind.False ||
                region.GetProperty("changedVertexCount").GetInt32()!=1361 || !Within(region,"maxOffsetM",0.05,0.050001)) return problem;
            foreach (var key in new[]{"topologyExact","uvExact","colorsExact","weightsExact","transformsExact",
                "untouchedPositionsExact","positionsWithinAuthorizedRegion","maxOffsetWithinLimit","morphDeltasPreserved",
                "untouchedShapePositionsExact","normalsOutsideSupportExact","rawPackedNormalsOutsideSupportExact",
                "exportCornerCorrespondenceExact","exportProtectedAttributesExact","exportPositionsMatchCandidate",
                "exportUntouchedPositionsExact","exportSkinAttributesExact","exportMorphDeltasPreserved","exportNormalsOutsideSupportExact","liveGlbRegionPass"})
                if (!True(region,key)) return problem;
            if (Text(consistency,"schema") != "horse.foal_idle.walk_consistency.v1" || !Passed(consistency) ||
                Text(consistency,"candidateBlendSha256") != Blend || Text(consistency,"candidateGlbSha256") != Glb ||
                Text(consistency,"acceptedWalkGlbSha256") != WalkGlb) return problem;
            foreach (var key in new[]{"glbRestPositionsExact","glbIndexedTopologyExact","glbUvExact","glbColorsExact",
                "glbNormalsExact","glbMorphTargetsPreserved","blendShapeEndpointsExact","blendRestPositionsExact",
                "blendUvExact","blendColorsExact","blendNormalsExact","staticRigAndWeightsExact",
                "staticNodesAndMatricesExact","staticMaterialsExact","idleAnimationExact"})
                if (!True(consistency,key)) return problem;
            if (Text(morph,"schema") != "horse.foal_idle.morph_deformation.v1" || !Passed(morph) ||
                Text(morph,"candidateBlendSha256") != Blend || morph.GetProperty("caseCount").GetInt32()!=25 ||
                morph.GetProperty("sampleCountPerCase").GetInt32()!=121 || morph.GetProperty("cases").GetArrayLength()!=25) return problem;
            foreach (var row in morph.GetProperty("cases").EnumerateArray())
            {
                if (!True(row,"allFinite") || !Within(row,"minimumSoleHeightM",-.002,.003) ||
                    !Within(row,"maximumSoleMinimumHeightM",-.002,.003) ||
                    !Within(row,"maximumFootMotionM",0,.005) || !Within(row,"maximumLoopErrorM",0,.001)) return problem;
                foreach(var p in row.GetProperty("maxEdgeStretch").EnumerateObject())
                    if (!Within(row.GetProperty("maxEdgeStretch"),p.Name,0,2)) return problem;
                foreach(var p in row.GetProperty("maxAttachmentDriftM").EnumerateObject())
                    if (!Within(row.GetProperty("maxAttachmentDriftM"),p.Name,0,.002)) return problem;
            }
            using var importedDoc=JsonDocument.Parse(FileAccess.GetFileAsBytes(Root+"native-idle-report.json"));
            var imported=importedDoc.RootElement;
            if (Text(imported,"candidateGlbSha256")!=Glb || !True(imported,"usesProjectImport") ||
                imported.GetProperty("failures").GetArrayLength()!=0 || !Within(imported,"projectImportFps",30,30) ||
                imported.GetProperty("animationOptimizerEnabled").GetBoolean() || !Within(imported,"meshCount",4,4)) return problem;
            foreach(var mesh in imported.GetProperty("meshes").EnumerateArray())
                if (!Within(mesh,"blendShapes",4,4) || !Within(mesh,"zeroWeightVertices",0,0) || !Within(mesh,"nonUnitWeightVertices",0,0)) return problem;
            var soles=imported.GetProperty("soleSupport");
            if (soles.GetProperty("failures").GetArrayLength()!=0 || !Within(soles,"sampleCount",121,121) ||
                !Within(soles,"minimumHeightM",-.002,.003) || !Within(soles,"maximumFrameSoleMinimumM",-.002,.003) ||
                !Within(soles,"maximumPointMotionM",0,.005) || !Within(soles,"loopErrorM",0,.001)) return problem;
            foreach(var part in imported.GetProperty("allComponentDeformation").EnumerateObject())
                if (part.Value.GetProperty("failures").GetArrayLength()!=0 || !Within(part.Value,"maxBodyEdgeStretch",0,2)) return problem;
            if (imported.GetProperty("attachments").GetProperty("failures").GetArrayLength()!=0) return problem;
            return null;
        }
        catch(Exception) { return problem; }
    }
    private static string? Text(JsonElement e,string k)=>e.GetProperty(k).GetString();
    private static bool True(JsonElement e,string k)=>e.GetProperty(k).ValueKind==JsonValueKind.True;
    private static bool Passed(JsonElement e)=>True(e,"pass") && e.GetProperty("failures").GetArrayLength()==0;
    private static bool Within(JsonElement e,string k,double min,double max)
    { double n=e.GetProperty(k).GetDouble();return double.IsFinite(n)&&n>=min&&n<=max; }
    private static string Hash(string p)=>FileAccess.FileExists(p)?Convert.ToHexString(SHA256.HashData(FileAccess.GetFileAsBytes(p))).ToLowerInvariant():"";
}
