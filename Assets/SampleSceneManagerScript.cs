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
    /// NavMesh作成の対象となるオブジェクトに付けるレイヤーの名前
    /// </summary>
    public string NavMeshTargetLayerName = "NavMeshTarget";

    /// <summary>
    /// マップ1マスの長さ
    /// </summary>
    public float MapTileSize = 1f;

    /// <summary>
    /// 外周となる壁の幅
    /// </summary>
    public int OuterWallWidth = 2;

    /// <summary>
    /// 壁を置く割合
    /// </summary>
    public int WallPlacemantRate = 10;

    /// <summary>
    /// 壁の隙間を埋める処理の回数
    /// </summary>
    public int GrapFillingLoopCount = 1;

    /// <summary>
    /// エージェントとゴールを移動可能な地点にずらす際に探す範囲
    /// </summary>
    public float ObjectPositioningRange = 1;

    /// <summary>
    /// 到達できない場所を削除
    /// </summary>
    public bool RemoveUnreachableLocations = true;

    /// <summary>
    /// 外周を海にするかどうか
    /// </summary>
    public bool AddSea = true;

    /// <summary>
    /// 孤立した壁を草にするかどうか
    /// </summary>
    public bool AddWeed = true;

    void Start()
    {
        Initialize();
    }

    // NavMesh対象オブジェクトに付ける必要があるレイヤー
    int navMeshTargetLayer;

    // タイル数
    int mapWidth;
    int mapDepth;

    // オブジェクト配置基準
    float basePosX;
    float basePosZ;

    // マップデータ
    byte[,] map;

    // マップ地点毎の処理用
    List<(int x, int z)> collectedPoints = new List<(int x, int z)>();
    bool[,] checkedMap;

    const byte pointTypeEmpty = 0;// 空き地
    const byte pointTypeWall = 1;// 壁
    const byte pointTypeSea = 2;// 海
    const byte pointTypeWeed = 3;// 草

    void Initialize()
    {
        // NavMesh対象オブジェクトに付ける必要があるレイヤー
        navMeshTargetLayer = LayerMask.GetMask(NavMeshTargetLayerName);

        // NavMesh対象オブジェクトの配置範囲
        var navMeshBounds = NavMeshSurface.navMeshData.sourceBounds;

        // タイル数。+1は端数考慮
        mapWidth = Mathf.FloorToInt(navMeshBounds.size.x / MapTileSize) + 1;
        mapDepth = Mathf.FloorToInt(navMeshBounds.size.z / MapTileSize) + 1;

        Debug.Log($"map({mapWidth},{mapDepth})");

        // オブジェクト配置基準
        basePosX = Mathf.FloorToInt(navMeshBounds.min.x) + MapTileSize * 0.5f;
        basePosZ = Mathf.FloorToInt(navMeshBounds.min.z) + MapTileSize * 0.5f;

        // マップデータ
        map = new byte[mapWidth, mapDepth];

        // マップ地点毎の処理用
        checkedMap = new bool[mapWidth, mapDepth];

        // マップ内の各地点
        var mapPoints = Enumerable.Range(0, mapWidth)
            .SelectMany(x => Enumerable.Range(0, mapDepth)
                .Select(z =>
                (
                    x,
                    z,
                    ground: GetGroundPosition(x, z)
                )))
            .ToArray();

        // 指定地点の周囲にある物を数える
        var distance1Deltas = MakeNearPointDeltas(1)
            .Where(delta => !(delta.x == 0 && delta.z == 0))//周囲なので(0,0)を除外
            .ToArray();
        int GetAroundCount(int x, int z, byte pointType)
        {
            return distance1Deltas
                .Select(delta => (x: x + delta.x, z: z + delta.z))
                .Select(p => GetMapPointType(p.x, p.z))
                .Count(v => v == pointType);
        }

        // 海を配置するかどうか
        bool bAddSea = AddSea && SeaPrefab != null;

        // 草を配置するかどうか
        bool bAddWeed = AddWeed && WeedPrefab != null;

        //-----------------------------------------------------------------

        #region 地面が無い所を壁にする

        foreach (var (x, z, _) in mapPoints.Where(p => p.ground == null))
        {
            map[x, z] = pointTypeWall;
        }

        #endregion

        if (0 < OuterWallWidth)
        {
            #region マップ外及び壁（＝地面が無い）に近い場所を壁にする

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

        // 壁となった場所を退避して、海配置時に使う。
        var outerWallPoints = mapPoints.Where(p => map[p.x, p.z] == pointTypeWall).ToArray();

        #region ランダムに壁を配置
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

        #region 壁の隙間を埋める

        foreach (var loop in Enumerable.Range(0, GrapFillingLoopCount))
        {
            // 空き地の周囲に5つ以上壁があったら壁にする
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
            #region 外壁及びそれに隣接する壁を海に入れ替える

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
            #region 到達できない所を削除

            // 連続する空き地毎にグループ分け
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
                // 最も広い場所以外の空き地を壁で塗りつぶす
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

        #region 孤立している壁を変換
        {
            byte swapPointType = bAddWeed ? pointTypeWeed : pointTypeEmpty;

            // 壁に隣接している壁が2つ以下なら入れ替え。以降 1 0 と見ていく
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

        #region オブジェクト生成

        // 壁と海に NavMeshObstacle が付いているので NavMeshSurface が変化する

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

        // NavMesh更新
        NavMeshSurface.BuildNavMesh();

        //-----------------------------------------------------------------

        #region プレーヤーとゴールを歩ける場所に移動
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

    // 地面位置判定
    Vector3? GetGroundPosition(int x, int z)
    {
        // 対象オブジェクトより少し上から下に向かってレイを出す事で
        // その地点での一番高い場所を得る
        var origin = new Vector3(
            basePosX + x * MapTileSize,
            NavMeshSurface.navMeshData.sourceBounds.max.y + 1f,
            basePosZ + z * MapTileSize);

        var r = new Ray(origin, Vector3.down);

        return Physics.Raycast(r, out var hit, Mathf.Infinity, navMeshTargetLayer)
            ? hit.point
            : (Vector3?)null;
    }

    // マップ内かどうか
    bool IsInMap(int x, int z)
    {
        return 0 <= x && x < mapWidth &&
               0 <= z && z < mapDepth;
    }

    // 指定地点の種類を得る
    byte GetMapPointType(int x, int z)
    {
        return IsInMap(x, z)
            ? map[x, z]
            : pointTypeWall; // マップ外は壁
    }

    // 周囲探索用
    (int x, int z)[] MakeNearPointDeltas(int distance)
    {
        var values = Enumerable.Range(-distance, distance * 2 + 1).ToArray();
        return values
            .SelectMany(x => values.Select(z => (x, z)))
            .ToArray();
    }

    // 指定タイプが連続する地点を取得
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
