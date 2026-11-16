using System.Collections.Generic;
using UnityEngine;

namespace Gameplay.Level
{
    /// <summary>
    /// Quick test level: builds a random street grid when the game starts. Roads with lane markings, city blocks
    /// with buildings, parks, rideable ramps up onto rooftops and mounds, plus road obstacles (kickers, road hills,
    /// speed bumps, parked cars, knockable cones). The player starts at the central intersection, which is
    /// centred on this object's position. Everything is primitives built in code; nothing is saved to the scene.
    /// </summary>
    public class StreetLevelGenerator : MonoBehaviour
    {
        [Tooltip("Same number = same city every time. 0 = a new random city every play.")]
        [SerializeField] private int seed;

        [Tooltip("How many city blocks along each side. 6 = a 6×6 grid.")]
        [SerializeField, Range(2, 12)] private int blocksPerSide = 6;

        [Tooltip("Size of one city block, in metres (a real downtown block ≈ 80–100 m; smaller = more corners).")]
        [SerializeField, Min(10f)] private float blockSize = 40f;

        [Tooltip("Width of the roads, in metres (two-lane street ≈ 8–10 m, avenue ≈ 15–20 m).")]
        [SerializeField, Min(4f)] private float roadWidth = 14f;

        [Tooltip("Chance (%) that a road segment between two intersections gets an obstacle or ramp.")]
        [SerializeField, Range(0f, 100f)] private float roadObstaclePercent = 60f;

        [Tooltip("Scene objects to switch off when the city is built (e.g. old test ramps that would end up inside buildings).")]
        [SerializeField] private GameObject[] hideOnStart;

        private const float CurbHeight = 0.12f;
        private const float SidewalkWidth = 3f;

        private static readonly Color Asphalt = new(0.22f, 0.22f, 0.24f);
        private static readonly Color Sidewalk = new(0.62f, 0.62f, 0.6f);
        private static readonly Color Marking = new(0.95f, 0.9f, 0.6f);
        private static readonly Color Grass = new(0.35f, 0.6f, 0.3f);
        private static readonly Color Trunk = new(0.4f, 0.28f, 0.18f);
        private static readonly Color Leaves = new(0.25f, 0.5f, 0.25f);
        private static readonly Color RampColor = new(1f, 0.55f, 0.15f);
        private static readonly Color ConeColor = new(1f, 0.4f, 0.05f);
        private static readonly Color BumpYellow = new(1f, 0.85f, 0.1f);
        private static readonly Color BumpBlack = new(0.08f, 0.08f, 0.08f);
        private static readonly Color[] BuildingColors =
        {
            new(0.85f, 0.75f, 0.6f), new(0.7f, 0.45f, 0.35f), new(0.55f, 0.6f, 0.7f),
            new(0.8f, 0.8f, 0.78f), new(0.45f, 0.5f, 0.45f), new(0.9f, 0.85f, 0.7f),
        };
        private static readonly Color[] CarColors =
        {
            new(0.8f, 0.1f, 0.1f), new(0.1f, 0.3f, 0.8f), new(0.95f, 0.95f, 0.95f),
            new(0.1f, 0.1f, 0.1f), new(0.9f, 0.75f, 0.1f), new(0.3f, 0.6f, 0.35f),
        };

        private readonly List<Vector3> _deliverySpots = new();
        private System.Random _random;
        private Transform _root;
        private int _groundLayer;

        private float Pitch => blockSize + roadWidth;

        /// <summary>
        /// World positions on the sidewalks, by the road, that are always clear (for delivery pickups and drop-offs).
        /// Filled in Awake.
        /// </summary>
        public IReadOnlyList<Vector3> DeliverySpots => _deliverySpots;

        private void Awake()
        {
            foreach (GameObject go in hideOnStart)
                if (go) go.SetActive(false);

            int usedSeed = seed != 0 ? seed : System.Environment.TickCount;
            Debug.Log($"[StreetLevelGenerator] Seed {usedSeed} (put it in Seed to get this city again).", this);
            _random = new System.Random(usedSeed);

            _groundLayer = LayerMask.NameToLayer("Ground");
            if (_groundLayer < 0) _groundLayer = 0;

            _root = new GameObject("Generated City").transform;
            _root.SetPositionAndRotation(new Vector3(transform.position.x, 0f, transform.position.z), Quaternion.identity);

            Build();
        }

        #region Layout

        private void Build()
        {
            int half = blocksPerSide / 2;
            int min = -half, max = blocksPerSide - half; // intersection indices; 0 = the centre one

            // Asphalt under everything (thin, so it sits just above the scene's floor)
            Box("Asphalt", new Vector3((min + max) * 0.5f * Pitch, -0.05f + 0.01f, (min + max) * 0.5f * Pitch),
                new Vector3((max - min) * Pitch + roadWidth, 0.1f, (max - min) * Pitch + roadWidth), Asphalt);

            for (int x = min; x < max; x++)
            for (int z = min; z < max; z++)
                BuildBlock(new Vector3((x + 0.5f) * Pitch, 0f, (z + 0.5f) * Pitch));

            // Road segments between intersections, both directions
            for (int a = min; a <= max; a++)
            for (int b = min; b < max; b++)
            {
                BuildRoadSegment(new Vector3((b + 0.5f) * Pitch, 0f, a * Pitch), Vector3.right);
                BuildRoadSegment(new Vector3(a * Pitch, 0f, (b + 0.5f) * Pitch), Vector3.forward);
            }

        }

        private void BuildBlock(Vector3 centre)
        {
            // Raised sidewalk slab covering the block
            Box("Sidewalk", centre + Vector3.up * CurbHeight * 0.5f, new Vector3(blockSize, CurbHeight, blockSize), Sidewalk);
            float inner = blockSize - SidewalkWidth * 2f;
            Vector3 top = centre + Vector3.up * CurbHeight;

            // One delivery spot on the sidewalk along each side of the block (the sidewalk ring is never built on)
            for (int side = 0; side < 4; side++)
            {
                Quaternion facing = Quaternion.Euler(0f, 90f * side, 0f);
                Vector3 local = top + facing * new Vector3(Range(-inner / 3f, inner / 3f), 0f, blockSize / 2f - SidewalkWidth / 2f);
                _deliverySpots.Add(_root.TransformPoint(local));
            }

            float roll = Range(0f, 1f);
            if (roll < 0.55f) Buildings(top, inner);
            else if (roll < 0.72f) Park(top, inner);
            else if (roll < 0.88f) RooftopRamp(top, inner);
            else Mound(top, inner);
        }

        private void Buildings(Vector3 top, float inner)
        {
            // 1, 2 or 4 lots, with a small gap (alley) between them
            int split = Pick(1, 2, 2, 2);
            float gap = split > 1 ? 2f : 0f;
            float lot = (inner - gap * (split - 1)) / split;
            for (int i = 0; i < split; i++)
            for (int j = 0; j < split; j++)
            {
                float h = Range(6f, 35f);
                float size = lot * Range(0.85f, 1f);
                Vector3 c = top + new Vector3(-inner / 2f + lot / 2f + i * (lot + gap), h / 2f, -inner / 2f + lot / 2f + j * (lot + gap));
                Box("Building", c, new Vector3(size, h, size), PickColor(BuildingColors));
            }
        }

        private void Park(Vector3 top, float inner)
        {
            Box("Grass", top + Vector3.up * 0.01f, new Vector3(inner, 0.02f, inner), Grass, collide: false);

            int trees = _random.Next(5, 12);
            for (int i = 0; i < trees; i++)
                Tree(top + new Vector3(Range(-inner / 2f + 2f, inner / 2f - 2f), 0f, Range(-inner / 2f + 2f, inner / 2f - 2f)));

            // A couple of small kickers to play with
            for (int i = 0; i < 2; i++)
                Wedge("Kicker", top + new Vector3(Range(-inner / 3f, inner / 3f), 0f, Range(-inner / 3f, inner / 3f)),
                    Quaternion.Euler(0f, Range(0f, 360f), 0f), 3f, 2.5f, Range(0.6f, 1.2f), RampColor);
        }

        /// <summary>Long ramp up onto a flat platform, with a drop off the far side.</summary>
        private void RooftopRamp(Vector3 top, float inner)
        {
            float height = Range(2.5f, 5f);
            float rampLength = height / Mathf.Tan(Range(12f, 18f) * Mathf.Deg2Rad);
            rampLength = Mathf.Min(rampLength, inner * 0.6f);
            float platform = inner - rampLength;

            Quaternion facing = Quaternion.Euler(0f, 90f * _random.Next(4), 0f);
            Vector3 rampStart = top + facing * new Vector3(0f, 0f, -inner / 2f);
            Wedge("Rooftop Ramp", rampStart, facing, rampLength, inner * 0.5f, height, RampColor);
            Box("Rooftop", top + facing * new Vector3(0f, height / 2f, -inner / 2f + rampLength + platform / 2f),
                facing, new Vector3(inner * 0.8f, height, platform), PickColor(BuildingColors));
        }

        /// <summary>A hill: ramp up, short flat top, ramp down.</summary>
        private void Mound(Vector3 top, float inner)
        {
            float height = Range(1.5f, 3.5f);
            float ramp = Mathf.Min(height / Mathf.Tan(Range(10f, 20f) * Mathf.Deg2Rad), inner * 0.4f);
            float flat = inner - ramp * 2f;
            Quaternion facing = Quaternion.Euler(0f, 90f * _random.Next(4), 0f);
            float width = inner * 0.7f;

            Wedge("Mound Up", top + facing * new Vector3(0f, 0f, -inner / 2f), facing, ramp, width, height, Grass);
            Box("Mound Top", top + facing * new Vector3(0f, height / 2f, 0f), facing, new Vector3(width, height, flat), Grass);
            Wedge("Mound Down", top + facing * new Vector3(0f, 0f, inner / 2f), facing * Quaternion.Euler(0f, 180f, 0f), ramp, width, height, Grass);
        }

        private void BuildRoadSegment(Vector3 centre, Vector3 along)
        {
            Quaternion facing = Quaternion.LookRotation(along);
            Vector3 side = facing * Vector3.right;

            // Dashed centre line (visual only)
            for (float d = -blockSize / 2f + 1.5f; d < blockSize / 2f; d += 6f)
                Box("Marking", centre + along * (d + 1.5f) + Vector3.up * 0.015f, facing, new Vector3(0.15f, 0.01f, 3f), Marking, collide: false);

            if (Range(0f, 100f) > roadObstaclePercent)
                return;

            float lane = roadWidth / 4f; // offset from the centre line to the middle of a lane
            float direction = _random.Next(2) == 0 ? 1f : -1f;
            Quaternion travel = Quaternion.LookRotation(along * direction);

            switch (_random.Next(5))
            {
                case 0: // kicker in one lane
                    Wedge("Kicker", centre + side * lane * Pick(-1f, 1f) - along * direction * 2f, travel,
                        Range(3f, 5f), lane * 1.6f, Range(0.8f, 1.6f), RampColor);
                    break;

                case 1: // road hill across the whole road
                {
                    float h = Range(1f, 2.5f);
                    float ramp = Mathf.Min(h / Mathf.Tan(12f * Mathf.Deg2Rad), blockSize * 0.35f);
                    float flat = Mathf.Max(blockSize - ramp * 2f - 4f, 2f);
                    Wedge("Road Hill Up", centre - along * (flat / 2f + ramp), facing, ramp, roadWidth, h, Asphalt);
                    Box("Road Hill Top", centre + Vector3.up * h / 2f, facing, new Vector3(roadWidth, h, flat), Asphalt);
                    Wedge("Road Hill Down", centre + along * (flat / 2f + ramp), facing * Quaternion.Euler(0f, 180f, 0f), ramp, roadWidth, h, Asphalt);
                    break;
                }

                case 2: // speed bumps: too fast over one throws the rider off (see CrashBump)
                    for (int i = -1; i <= 1; i += 2)
                    {
                        Vector3 bumpCentre = centre + along * i * blockSize * 0.25f;
                        Box("Speed Bump", bumpCentre + Vector3.up * 0.09f, facing, new Vector3(roadWidth, 0.18f, 0.9f), BumpYellow)
                            .AddComponent<CrashBump>();

                        // Black warning stripes on top (visual only)
                        for (float s = -roadWidth / 2f + 0.5f; s < roadWidth / 2f; s += 2f)
                            Box("Stripe", bumpCentre + side * (s + 0.5f) + Vector3.up * 0.185f, facing,
                                new Vector3(1f, 0.01f, 0.92f), BumpBlack, collide: false);
                    }
                    break;

                case 3: // parked cars along the curb
                {
                    float curb = roadWidth / 2f - 1.2f;
                    for (float d = -blockSize / 2f + 4f; d < blockSize / 2f - 4f; d += Range(5.5f, 9f))
                    {
                        if (Range(0f, 1f) < 0.3f) continue;
                        Car(centre + along * d + side * curb * Pick(-1f, 1f), facing);
                    }
                    break;
                }

                default: // a row of cones to knock over
                {
                    Vector3 offset = side * lane * Pick(-1f, 0f, 1f);
                    for (int i = 0; i < 6; i++)
                        Cone(centre + offset + along * (i - 2.5f) * 2.5f);
                    break;
                }
            }
        }

        #endregion

        #region Props

        private void Tree(Vector3 basePos)
        {
            float h = Range(3f, 5f);
            Primitive(PrimitiveType.Cylinder, "Tree", basePos + Vector3.up * h / 2f, Quaternion.identity,
                new Vector3(0.4f, h / 2f, 0.4f), Trunk);
            GameObject leaves = Primitive(PrimitiveType.Sphere, "Leaves", basePos + Vector3.up * (h + 1f), Quaternion.identity,
                Vector3.one * Range(2.5f, 4f), Leaves);
            DestroyImmediate(leaves.GetComponent<Collider>());
        }

        private void Car(Vector3 pos, Quaternion facing)
        {
            Color color = PickColor(CarColors);
            Box("Car", pos + Vector3.up * 0.55f, facing, new Vector3(1.8f, 0.9f, 4.3f), color);
            Box("Car Cabin", pos + facing * new Vector3(0f, 1.3f, -0.2f), facing, new Vector3(1.6f, 0.65f, 2.2f), color);
        }

        private void Cone(Vector3 pos)
        {
            GameObject cone = Primitive(PrimitiveType.Cylinder, "Cone", pos + Vector3.up * 0.4f, Quaternion.identity,
                new Vector3(0.4f, 0.4f, 0.4f), ConeColor);
            cone.layer = 0; // movable, so not ground
            var body = cone.AddComponent<Rigidbody>();
            body.mass = 0.5f;
        }

        #endregion

        #region Building blocks

        private GameObject Box(string name, Vector3 centre, Vector3 size, Color color, bool collide = true) =>
            Box(name, centre, Quaternion.identity, size, color, collide);

        private GameObject Box(string name, Vector3 centre, Quaternion rotation, Vector3 size, Color color, bool collide = true)
        {
            GameObject go = Primitive(PrimitiveType.Cube, name, centre, rotation, size, color);
            if (!collide) DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        private GameObject Primitive(PrimitiveType type, string name, Vector3 localPos, Quaternion rotation, Vector3 scale, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.layer = _groundLayer;
            go.transform.SetParent(_root, false);
            go.transform.SetLocalPositionAndRotation(localPos, rotation);
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = MaterialFor(color);
            return go;
        }

        /// <summary>
        /// Ramp shaped like a doorstop: starts at ground level at <paramref name="start"/> and rises along the
        /// rotation's forward to <paramref name="height"/> after <paramref name="length"/> metres.
        /// </summary>
        private GameObject Wedge(string name, Vector3 start, Quaternion rotation, float length, float width, float height, Color color)
        {
            var go = new GameObject(name) { layer = _groundLayer };
            go.transform.SetParent(_root, false);
            go.transform.SetLocalPositionAndRotation(start, rotation);

            Mesh mesh = WedgeMesh(length, width, height);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = MaterialFor(color);
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }

        private static Mesh WedgeMesh(float length, float width, float height)
        {
            float w = width / 2f;
            Vector3 a = new(-w, 0f, 0f), b = new(w, 0f, 0f);         // front edge, on the ground
            Vector3 c = new(-w, 0f, length), d = new(w, 0f, length); // back, on the ground
            Vector3 e = new(-w, height, length), f = new(w, height, length); // back, at the top
            Vector3 centroid = (a + b + c + d + e + f) / 6f;

            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            void Face(params Vector3[] corners)
            {
                // Flat-shaded: own vertices per face. Unity shows the side where the corners go clockwise, which is the side
                // Cross(b - a, c - a) points to; flip the order if that side faces the inside of the wedge.
                Vector3 normal = Vector3.Cross(corners[1] - corners[0], corners[2] - corners[0]);
                Vector3 faceCentre = Vector3.zero;
                foreach (Vector3 p in corners) faceCentre += p;
                faceCentre /= corners.Length;
                if (Vector3.Dot(normal, faceCentre - centroid) < 0f)
                    System.Array.Reverse(corners);

                int first = vertices.Count;
                vertices.AddRange(corners);
                for (int i = 1; i < corners.Length - 1; i++)
                    triangles.AddRange(new[] { first, first + i, first + i + 1 });
            }

            Face(a, b, f, e); // slope
            Face(c, d, f, e); // back wall
            Face(a, b, d, c); // bottom
            Face(a, c, e);    // left side
            Face(b, d, f);    // right side

            var mesh = new Mesh { name = "Wedge" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Material MaterialFor(Color color) => ColorMaterials.Lit(color);

        #endregion

        #region Random

        private float Range(float min, float max) => min + (float)_random.NextDouble() * (max - min);
        private T Pick<T>(params T[] options) => options[_random.Next(options.Length)];
        private Color PickColor(Color[] palette) => palette[_random.Next(palette.Length)];

        #endregion
    }
}
