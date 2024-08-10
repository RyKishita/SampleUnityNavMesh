using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

public class SampleSceneManagerScript : MonoBehaviour
{
    public Unity.AI.Navigation.NavMeshSurface NavMeshSurface;
    public GameObject GeneratedObjects;
    public GameObject Agent;
    public GameObject Goal;
    public GameObject WallPrefab;
    public GameObject WeedPrefab;
    public GameObject SeaPrefab;

    /// <summary>
    /// NavMesh�쐬�̑ΏۂƂȂ�I�u�W�F�N�g�ɕt���郌�C���[�̖��O
    /// </summary>
    public string NavMeshTargetLayerName = "NavMeshTarget";

    /// <summary>
    /// �}�b�v1�}�X�̒���
    /// </summary>
    public float MapTileSize = 1f;

    /// <summary>
    /// �O���ƂȂ�ǂ̕�
    /// </summary>
    public int OuterWallWidth = 2;

    /// <summary>
    /// �ǂ�u������
    /// </summary>
    public int WallPlacemantRate = 10;

    /// <summary>
    /// �ǂ̌��Ԃ𖄂߂鏈���̉�
    /// </summary>
    public int GrapFillingLoopCount = 1;

    /// <summary>
    /// �G�[�W�F���g�ƃS�[�����ړ��\�Ȓn�_�ɂ��炷�ۂɒT���͈�
    /// </summary>
    public float ObjectPositioningRange = 1;

    /// <summary>
    /// ���B�ł��Ȃ��ꏊ���폜
    /// </summary>
    public bool RemoveUnreachableLocations = true;

    /// <summary>
    /// �O�����C�ɂ��邩�ǂ���
    /// </summary>
    public bool AddSea = true;

    /// <summary>
    /// �Ǘ������ǂ𑐂ɂ��邩�ǂ���
    /// </summary>
    public bool AddWeed = true;

    void Start()
    {
        Initialize();
    }

    // NavMesh�ΏۃI�u�W�F�N�g�ɕt����K�v�����郌�C���[
    int navMeshTargetLayer;

    // �^�C����
    int mapWidth;
    int mapDepth;

    // �I�u�W�F�N�g�z�u�
    float basePosX;
    float basePosZ;

    // �}�b�v�f�[�^
    byte[,] map;

    // �}�b�v�n�_���̏����p
    List<(int x, int z)> collectedPoints = new List<(int x, int z)>();
    bool[,] checkedMap;

    const byte pointTypeEmpty = 0;// �󂫒n
    const byte pointTypeWall = 1;// ��
    const byte pointTypeSea = 2;// �C
    const byte pointTypeWeed = 3;// ��

    void Initialize()
    {
        // NavMesh�ΏۃI�u�W�F�N�g�ɕt����K�v�����郌�C���[
        navMeshTargetLayer = LayerMask.GetMask(NavMeshTargetLayerName);

        // NavMesh�ΏۃI�u�W�F�N�g�̔z�u�͈�
        var navMeshBounds = NavMeshSurface.navMeshData.sourceBounds;

        // �^�C�����B+1�͒[���l��
        mapWidth = Mathf.FloorToInt(navMeshBounds.size.x / MapTileSize) + 1;
        mapDepth = Mathf.FloorToInt(navMeshBounds.size.z / MapTileSize) + 1;

        Debug.Log($"map({mapWidth},{mapDepth})");

        // �I�u�W�F�N�g�z�u�
        basePosX = Mathf.FloorToInt(navMeshBounds.min.x) + MapTileSize * 0.5f;
        basePosZ = Mathf.FloorToInt(navMeshBounds.min.z) + MapTileSize * 0.5f;

        // �}�b�v�f�[�^
        map = new byte[mapWidth, mapDepth];

        // �}�b�v�n�_���̏����p
        checkedMap = new bool[mapWidth, mapDepth];

        // �}�b�v���̊e�n�_
        var mapPoints = Enumerable.Range(0, mapWidth)
            .SelectMany(x => Enumerable.Range(0, mapDepth)
                .Select(z =>
                (
                    x,
                    z,
                    ground: GetGroundPosition(x, z)
                )))
            .ToArray();

        // �w��n�_�̎��͂ɂ��镨�𐔂���
        var distance1Deltas = MakeNearPointDeltas(1)
            .Where(delta => !(delta.x == 0 && delta.z == 0))//���͂Ȃ̂�(0,0)�����O
            .ToArray();
        int GetAroundCount(int x, int z, byte pointType)
        {
            return distance1Deltas
                .Select(delta => (x: x + delta.x, z: z + delta.z))
                .Select(p => GetMapPointType(p.x, p.z))
                .Count(v => v == pointType);
        }

        // �C��z�u���邩�ǂ���
        bool bAddSea = AddSea && SeaPrefab != null;

        // ����z�u���邩�ǂ���
        bool bAddWeed = AddWeed && WeedPrefab != null;

        //-----------------------------------------------------------------

        #region �n�ʂ���������ǂɂ���

        foreach (var (x, z, _) in mapPoints.Where(p => p.ground == null))
        {
            map[x, z] = pointTypeWall;
        }

        #endregion

        if (0 < OuterWallWidth)
        {
            #region �}�b�v�O�y�ѕǁi���n�ʂ������j�ɋ߂��ꏊ��ǂɂ���

            var aroundDeltas = MakeNearPointDeltas(OuterWallWidth);
            var points = mapPoints.Where(p =>
                    aroundDeltas
                        .Select(delta => GetMapPointType(p.x + delta.x, p.z + delta.z))
                        .Contains(pointTypeWall)
                ).ToArray();
            foreach (var (x, z, ground) in points)
            {
                map[x, z] = pointTypeWall;
            }

            #endregion
        }

        // �ǂƂȂ����ꏊ��ޔ����āA�C�z�u���Ɏg���B
        var outerWallPoints = mapPoints.Where(p => map[p.x, p.z] == pointTypeWall).ToArray();

        #region �����_���ɕǂ�z�u
        {
            var random = new System.Random();
            foreach (var (x, z, _) in mapPoints
                .Where(p => map[p.x, p.z] != pointTypeWall)
                .Where(_ => random.Next(100) < WallPlacemantRate))
            {
                map[x, z] = pointTypeWall;
            }
        }
        #endregion

        #region �ǂ̌��Ԃ𖄂߂�

        foreach (var loop in Enumerable.Range(0, GrapFillingLoopCount))
        {
            // �󂫒n�̎��͂�5�ȏ�ǂ���������ǂɂ���
            var points = mapPoints
                .Where(p => map[p.x, p.z] == pointTypeEmpty)
                .Where(p => 5 <= GetAroundCount(p.x, p.z, pointTypeWall))
                .ToList();

            Debug.Log($"grap filling step[{loop}] count={points.Count}");
            if (points.Count == 0) break;

            foreach (var (x, z, _) in points)
            {
                map[x, z] = pointTypeWall;
            }
        }

        #endregion

        if (bAddSea)
        {
            #region �O�ǋy�т���ɗאڂ���ǂ��C�ɓ���ւ���

            ReadyCollectContiguousPoints();
            foreach (var (x, z, _) in outerWallPoints)
            {
                CollectContiguousPoints(x, z, pointTypeWall);
            }

            foreach (var (x, z) in collectedPoints)
            {
                map[x, z] = pointTypeSea;
            }

            #endregion
        }

        if (RemoveUnreachableLocations)
        {
            #region ���B�ł��Ȃ������폜

            // �A������󂫒n���ɃO���[�v����
            var pointGroups = new List<(int x, int z)[]>();

            ReadyCollectContiguousPoints();
            foreach (var (x, z, _) in mapPoints.Where(mapPoint => map[mapPoint.x, mapPoint.z] == pointTypeEmpty))
            {
                CollectContiguousPoints(x, z, pointTypeEmpty);
                if (collectedPoints.Any())
                {
                    pointGroups.Add(collectedPoints.ToArray());
                    collectedPoints.Clear();
                }
            }

            if (pointGroups.Any())
            {
                // �ł��L���ꏊ�ȊO�̋󂫒n��ǂœh��Ԃ�
                foreach (var (x, z) in pointGroups
                    .OrderByDescending(group => group.Length)
                    .Skip(1)
                    .SelectMany(group => group))
                {
                    map[x, z] = pointTypeWall;
                }
            }
            else
            {
                Debug.LogWarning("Not found Empty Points");
            }

            #endregion
        }

        #region �Ǘ����Ă���ǂ�ϊ�
        {
            byte swapPointType = bAddWeed ? pointTypeWeed : pointTypeEmpty;

            // �ǂɗאڂ��Ă���ǂ�2�ȉ��Ȃ����ւ��B�ȍ~ 1 0 �ƌ��Ă���
            foreach (var count in Enumerable.Range(0, 3).Reverse())
            {
                foreach (var (x, z, _) in mapPoints
                    .Where(p => map[p.x, p.z] == pointTypeWall)
                    .Where(p => GetAroundCount(p.x, p.z, pointTypeWall) <= count))
                {
                    map[x, z] = swapPointType;
                }
            }
        }
        #endregion

        #region �I�u�W�F�N�g����

        // �ǂƊC�� NavMeshObstacle ���t���Ă���̂� NavMeshSurface ���ω�����

        foreach (var (x, z, ground) in mapPoints.Where(p => p.ground.HasValue))
        {
            GameObject prefab;
            switch (map[x, z])
            {
                case pointTypeWall: prefab = WallPrefab; break;
                case pointTypeSea: prefab = SeaPrefab; break;
                case pointTypeWeed: prefab = WeedPrefab; break;
                default: continue;
            }

            var obj = Instantiate(prefab, ground.Value, Quaternion.identity, GeneratedObjects.transform);
            obj.transform.localScale = new Vector3(MapTileSize, MapTileSize, MapTileSize);
        }

        #endregion

        // NavMesh�X�V
        NavMeshSurface.BuildNavMesh();

        //-----------------------------------------------------------------

        #region �v���[���[�ƃS�[���������ꏊ�Ɉړ�
        {
            float range = Mathf.Max(MapTileSize, ObjectPositioningRange);
            string areaName = "Walkable";
            int areaMask = 1 << NavMesh.GetAreaFromName(areaName);

            if (NavMesh.SamplePosition(Goal.transform.position, out var hit, range, areaMask))
            {
                Goal.transform.position = hit.position;
            }
            else
            {
                Debug.LogWarning($"Not found nearest {areaName} area for Goal.");
            }

            if (NavMesh.SamplePosition(Agent.transform.position, out hit, range, areaMask))
            {
                Agent.transform.position = hit.position;
            }
            else
            {
                Debug.LogWarning($"Not found nearest {areaName} area for Agent.");
            }
        }
        #endregion
    }

    // �n�ʈʒu����
    Vector3? GetGroundPosition(int x, int z)
    {
        // �ΏۃI�u�W�F�N�g��菭���ォ�牺�Ɍ������ă��C���o������
        // ���̒n�_�ł̈�ԍ����ꏊ�𓾂�
        var origin = new Vector3(
            basePosX + x * MapTileSize,
            NavMeshSurface.navMeshData.sourceBounds.max.y + 1f,
            basePosZ + z * MapTileSize);

        var r = new Ray(origin, Vector3.down);

        return Physics.Raycast(r, out var hit, Mathf.Infinity, navMeshTargetLayer)
            ? hit.point
            : (Vector3?)null;
    }

    // �}�b�v�����ǂ���
    bool IsInMap(int x, int z)
    {
        return 0 <= x && x < mapWidth &&
               0 <= z && z < mapDepth;
    }

    // �w��n�_�̎�ނ𓾂�
    byte GetMapPointType(int x, int z)
    {
        return IsInMap(x, z)
            ? map[x, z]
            : pointTypeWall; // �}�b�v�O�͕�
    }

    // ���͒T���p
    (int x, int z)[] MakeNearPointDeltas(int distance)
    {
        var values = Enumerable.Range(-distance, distance * 2 + 1).ToArray();
        return values
            .SelectMany(x => values.Select(z => (x, z)))
            .ToArray();
    }

    // �w��^�C�v���A������n�_���擾
    void ReadyCollectContiguousPoints()
    {
        collectedPoints.Clear();
        Array.Clear(checkedMap, 0, checkedMap.Length);
    }

    void CollectContiguousPoints(int baseX, int baseZ, params byte[] pointTypes)
    {
        var queue = new Queue<(int x, int z)>();
        queue.Enqueue((baseX, baseZ));

        while (queue.Count > 0)
        {
            var pos = queue.Dequeue();

            if (!IsInMap(pos.x, pos.z)) continue;

            if (checkedMap[pos.x, pos.z]) continue;
            checkedMap[pos.x, pos.z] = true;

            if (!pointTypes.Contains(map[pos.x, pos.z])) continue;

            collectedPoints.Add((pos.x, pos.z));

            queue.Enqueue((pos.x - 1, pos.z));
            queue.Enqueue((pos.x + 1, pos.z));
            queue.Enqueue((pos.x, pos.z - 1));
            queue.Enqueue((pos.x, pos.z + 1));
        }
    }
}
