/*=============================================================================

 File   : [GameObjectPool.cs]
 Desc   : GameObjectのプールを管理する。
        : 使用方法
        : 1. プールを取得(Prefabを指定,Awakeなどで呼び出す)
        :    var pool = PoolRegistry.Get(prefab);
        : 2. オブジェクトを借りる
        :    var obj = pool.Rent(position, rotation);
        : 3. オブジェクトを返す
        :    obj.GetComponent<Poolable>().Release();
 ------------------------------------------------------------------------------

 Date   : 2026/05/25
 Author : Yokoyama Haruki
 
===============================================================================*/
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// プールされたオブジェクトのライフサイクルイベントを定義するインターフェース
/// OnRent: オブジェクトが借りられたときに呼び出される
/// OnReturn: オブジェクトが返されたときに呼び出される
/// </summary>
public interface IPoolLifecycle
{
    void OnRent();
    void OnReturn();
}

/// <summary>
/// プールされたオブジェクトにアタッチするコンポーネント
/// Release()を呼び出すとオブジェクトがプールに返される
/// </summary>
public sealed class Poolable : MonoBehaviour
{
    public GameObjectPool Pool { get; set; }

    public void Release()
    {
        Pool.Return(gameObject);
    }
}

/// <summary>
/// GameObjectのプールを管理するクラス
/// Prefabを指定してプールを作成し、オブジェクトの借り出しと返却を行う
/// </summary>
public sealed class GameObjectPool
{
    private readonly GameObject _prefab;                // プールするPrefab

    private readonly Queue<GameObject> _pool = new();   // プールされたオブジェクトのキュー

    private readonly Transform _root;                   // プールされたオブジェクトの親Transform

    // コンストラクタ
    public GameObjectPool(
        GameObject prefab,
        Transform parent = null,
        int initialSize = 10)
    {
        _prefab = prefab;

        // プール用の親オブジェクトを作成
        var rootObj =
            new GameObject(
                $"{prefab.name} Pool");

        // プール用の親オブジェクトを残す
        _root = rootObj.transform;

        // プール用の親オブジェクトを指定された親の子にする
        if (parent != null)
        {
            _root.SetParent(parent);
        }

        // 初期サイズ分のオブジェクトをプールする
        Warm(initialSize);
    }

    /// <summary>
    /// プールにオブジェクトを事前に作成しておく
    /// </summary>
    /// <param name="count">作成するオブジェクトの数</param>
    private void Warm(int count)
    {
        for (int i = 0; i < count; i++)
        {
            var obj = Create(
                Vector3.zero,
                Quaternion.identity);

            _pool.Enqueue(obj);
        }
    }

    /// <summary>
    /// 新しいオブジェクトを作成する
    /// Rent()でプールにオブジェクトがない場合に呼び出される
    /// </summary>
    /// <param name="position">作成するオブジェクトの位置</param>
    /// <param name="rotation">作成するオブジェクトの回転</param>
    /// <returns>作成されたオブジェクト</returns>
    private GameObject Create(
        Vector3 position,
        Quaternion rotation)
    {
        // オブジェクトの作成
        var obj = Object.Instantiate(
            _prefab,
            position,
            rotation,
            _root);

        // Poolableコンポーネントの追加
        var poolable = obj.GetComponent<Poolable>();
        if (poolable == null)
        {
            poolable = obj.AddComponent<Poolable>();
        }
        poolable.Pool = this;

        // 作成したオブジェクトは非アクティブにしておく
        obj.SetActive(false);

        return obj;
    }

    /// <summary>
    /// プールからオブジェクトを借りる
    /// Instantiateの代わりにこのメソッドを呼び出すことで、プールされたオブジェクトを再利用する
    /// </summary>
    /// <param name="position">オブジェクトの位置</param>
    /// <param name="rotation">オブジェクトの回転</param>
    /// <returns>借りたオブジェクト</returns>
    public GameObject Rent(
        Vector3 position,
        Quaternion rotation)
    {
        GameObject obj; // 借りるオブジェクト

        // プールにオブジェクトがある場合はそれを借りる
        if (_pool.Count > 0)
        {
            obj = _pool.Dequeue();

            obj.transform.SetPositionAndRotation(
                position,
                rotation);
        }
        else
        {// プールにオブジェクトがない場合は新しく作成する
            obj = Create(
                position,
                rotation);
        }

        // IPoolLifecycleを実装している場合はOnRentを呼び出す
        if (obj.TryGetComponent<IPoolLifecycle>(
            out var lifecycle))
        {
            lifecycle.OnRent();
        }

        obj.SetActive(true);

        return obj;
    }

    /// <summary>
    /// プールにオブジェクトを返す
    /// </summary>
    /// <param name="obj">返却するオブジェクト</param>
    public void Return(GameObject obj)
    {
        // IPoolLifecycleを実装している場合はOnReturnを呼び出す
        if (obj.TryGetComponent<IPoolLifecycle>(
            out var lifecycle))
        {
            lifecycle.OnReturn();
        }
        // オブジェクトをプールの親の子にする
        obj.transform.SetParent(_root);

        obj.SetActive(false);

        _pool.Enqueue(obj);
    }
}

/// <summary>
/// 複数のGameObjectPoolを管理するクラス
/// Prefabごとにプールを作成し、必要に応じてプールを取得できるようにする
/// </summary>
public static class PoolRegistry
{
    private static readonly Dictionary<int,GameObjectPool> Pools = new();   // PrefabのインスタンスIDをキーにしてGameObjectPoolを管理する辞書

    private static Transform _root; // プールの親Transform

    [RuntimeInitializeOnLoadMethod] // アプリケーションの起動時に呼び出されるメソッド
    private static void Init()
    {
        Pools.Clear();
        // プールの親オブジェクトを作成
        var rootObj =
            new GameObject("ObjectPools");

        // プールの親オブジェクトをシーン遷移で破棄されないようにする
        Object.DontDestroyOnLoad(rootObj);
        _root = rootObj.transform;
    }

    /// <summary>
    /// 指定されたPrefabに対応するGameObjectPoolを取得する
    /// 既にプールが存在する場合はそれを返し、存在しない場合は新しく作成して返す
    /// </summary>
    /// <param name="prefab"></param>
    /// <param name="initialSize"></param>
    /// <returns></returns>
    public static GameObjectPool Get(
        GameObject prefab,
        int initialSize = 10)
    {
        var id = prefab.GetInstanceID();
        // プールの検索
        if (Pools.TryGetValue(
            id,
            out var pool))
        {
            return pool;
        }

        // プールが存在しない場合は新しく作成する
        pool = new GameObjectPool(
            prefab,
            _root,
            initialSize);

        Pools.Add(id, pool);

        return pool;
    }
}