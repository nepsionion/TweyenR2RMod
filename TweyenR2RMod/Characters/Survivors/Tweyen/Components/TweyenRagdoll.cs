using RoR2;
using System.Collections.Generic;
using UnityEngine;

namespace TweyenR2RMod.Survivors.Tweyen.Components
{
    /// <summary>
    /// Builds a RoR2 ragdoll on Tweyen's model: rigidbodies, colliders and joints on her main GBFR bones, driven by the
    /// game's RagdollController (the death state, cloned from the Commando, ragdolls a model that has one).
    /// GBFR bone names: _000 pelvis, _003 chest, _005 head, _00b/_00c and _007/_008 upper arms and forearms (hands
    /// _00d/_009), _012/_013 and _00e/_00f thighs and calves (ankles _014/_010). Searched under "Armature" only: the
    /// bow's bones reuse the same names.
    /// </summary>
    public static class TweyenRagdoll
    {
        private struct Part
        {
            public string bone, parent, end;
            public float radius, mass;
            public float twist, swing;
            public Part(string bone, string parent, string end, float radius, float mass, float twist, float swing)
            {
                this.bone = bone; this.parent = parent; this.end = end;
                this.radius = radius; this.mass = mass; this.twist = twist; this.swing = swing;
            }
        }

        /// <summary>end: the bone the part reaches to (its capsule's length); null = a sphere on the bone.</summary>
        private static readonly Part[] parts =
        {
            new Part("_000", null, null, 0.14f, 3f, 0f, 0f),
            new Part("_003", "_000", "_005", 0.13f, 3f, 15f, 25f),
            new Part("_005", "_003", null, 0.11f, 1f, 30f, 40f),
            new Part("_00b", "_003", "_00c", 0.045f, 1f, 30f, 70f),
            new Part("_00c", "_00b", "_00d", 0.04f, 0.8f, 10f, 80f),
            new Part("_007", "_003", "_008", 0.045f, 1f, 30f, 70f),
            new Part("_008", "_007", "_009", 0.04f, 0.8f, 10f, 80f),
            new Part("_012", "_000", "_013", 0.07f, 2f, 20f, 60f),
            new Part("_013", "_012", "_014", 0.055f, 1.5f, 10f, 80f),
            new Part("_00e", "_000", "_00f", 0.07f, 2f, 20f, 60f),
            new Part("_00f", "_00e", "_010", 0.055f, 1.5f, 10f, 80f),
        };

        /// <summary>The hands, for keeping the bow in hand when she falls.</summary>
        public static readonly string[] hands = { "_00d", "_009" };

        public static void Setup(GameObject model)
        {
            Transform armature = model.transform.Find("Armature");
            if (!armature)
            {
                Log.Warning("Tweyen ragdoll: no Armature on the model");
                return;
            }
            var bones = new Dictionary<string, Transform>();
            foreach (Transform t in armature.GetComponentsInChildren<Transform>(true))
            {
                if (!bones.ContainsKey(t.name)) bones[t.name] = t;
            }

            var ragdollBones = new List<Transform>();
            var bodies = new Dictionary<string, Rigidbody>();
            foreach (Part part in parts)
            {
                if (!bones.TryGetValue(part.bone, out Transform bone)) continue;
                bone.gameObject.layer = LayerIndex.ragdoll.intVal;
                Rigidbody body = bone.gameObject.AddComponent<Rigidbody>();
                body.mass = part.mass;
                body.isKinematic = true;
                bodies[part.bone] = body;

                float scale = Mathf.Max(bone.lossyScale.x, 0.0001f);
                if (part.end != null && bones.TryGetValue(part.end, out Transform end))
                {
                    // a capsule from the bone to the next one, along the bone's nearest local axis
                    Vector3 local = bone.InverseTransformVector(end.position - bone.position);
                    int axis = DominantAxis(local);
                    CapsuleCollider capsule = bone.gameObject.AddComponent<CapsuleCollider>();
                    capsule.direction = axis;
                    capsule.center = local * 0.5f;
                    capsule.height = local.magnitude + 2f * part.radius / scale;
                    capsule.radius = part.radius / scale;
                    AddJoint(bone, part, bodies, axis);
                }
                else
                {
                    SphereCollider sphere = bone.gameObject.AddComponent<SphereCollider>();
                    sphere.radius = part.radius / scale;
                    // the head sphere sits a little above the head bone
                    if (part.parent != null) sphere.center = bone.InverseTransformVector(Vector3.up * part.radius * 0.7f);
                    AddJoint(bone, part, bodies, 1);
                }
                ragdollBones.Add(bone);
            }

            RagdollController ragdoll = model.GetComponent<RagdollController>() ?? model.AddComponent<RagdollController>();
            ragdoll.bones = ragdollBones.ToArray();
            ragdoll.componentsToDisableOnRagdoll = new MonoBehaviour[0];
        }

        private static void AddJoint(Transform bone, Part part, Dictionary<string, Rigidbody> bodies, int axis)
        {
            if (part.parent == null || !bodies.TryGetValue(part.parent, out Rigidbody parentBody)) return;
            CharacterJoint joint = bone.gameObject.AddComponent<CharacterJoint>();
            joint.connectedBody = parentBody;
            joint.axis = Axis(axis);
            joint.swingAxis = Axis((axis + 1) % 3);
            joint.lowTwistLimit = new SoftJointLimit { limit = -part.twist };
            joint.highTwistLimit = new SoftJointLimit { limit = part.twist };
            joint.swing1Limit = new SoftJointLimit { limit = part.swing };
            joint.swing2Limit = new SoftJointLimit { limit = part.swing };
            joint.enableProjection = true;
        }

        private static int DominantAxis(Vector3 v)
        {
            Vector3 a = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
            return a.x >= a.y && a.x >= a.z ? 0 : (a.y >= a.z ? 1 : 2);
        }

        private static Vector3 Axis(int i) => i == 0 ? Vector3.right : (i == 1 ? Vector3.up : Vector3.forward);
    }
}
