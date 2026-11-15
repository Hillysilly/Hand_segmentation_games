using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// A warning on the floor where an attack will land: a disc or a fan, faintly filled with a bright rim, and a
    /// second fill growing from the middle to the rim; when it gets there, the attack hits (shader Repulsor/Floor Mark).
    /// </summary>
    public sealed class FloorMark
    {
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int ArcId = Shader.PropertyToID("_Arc");
        static readonly int FillId = Shader.PropertyToID("_Fill");
        static readonly int AlphaId = Shader.PropertyToID("_Alpha");

        readonly Transform quad;
        readonly Renderer renderer;
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        readonly Color color;

        public bool Shown => renderer != null && renderer.enabled;

        public FloorMark(Transform parent, Color color)
        {
            this.color = color;
            var go = new GameObject("Floor Mark", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = Resources.Load<Mesh>("QuadMesh");
            renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = Resources.Load<Material>("FloorMark");
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.enabled = false;
            quad = go.transform;
        }

        /// <summary>
        /// A fan of <paramref name="halfArc"/> degrees either side of <paramref name="facing"/> (180 for a whole disc),
        /// <paramref name="radius"/> metres out from <paramref name="centre"/>; <paramref name="fill"/> is how close the
        /// attack is (0 just started, 1 now).
        /// </summary>
        public void Show(Vector3 centre, Vector3 facing, float radius, float halfArc, float fill, float strength = 1f)
        {
            if (renderer.sharedMaterial == null)
                return;
            Vector3 flat = Vector3.ProjectOnPlane(facing, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f)
                flat = Vector3.forward;
            quad.SetPositionAndRotation(centre, Quaternion.LookRotation(Vector3.down, flat.normalized));
            quad.localScale = new Vector3(radius * 2f, radius * 2f, 1f);
            block.SetColor(ColorId, color);
            block.SetFloat(ArcId, halfArc);
            block.SetFloat(FillId, Mathf.Clamp01(fill));
            block.SetFloat(AlphaId, strength);
            renderer.SetPropertyBlock(block);
            renderer.enabled = true;
        }

        public void Hide()
        {
            if (renderer != null)
                renderer.enabled = false;
        }

        public void Destroy()
        {
            if (quad != null)
                Object.Destroy(quad.gameObject);
        }
    }
}
