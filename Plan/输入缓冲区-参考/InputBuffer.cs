/*
*****************************************************
*   ██╗  ██╗     ██╗███╗  ███╗  ███╗  ███╗         *
*   ██║  ██║     ██║████╗ ████║████╗ ████║         *
*   ███████║     ██║██╔████╔██║██╔████╔██║         *
*   ██╔══██║██   ██║██║╚██╔╝██║██║╚██╔╝██║         *
*   ██║  ██║╚█████╔╝██║ ╚═╝ ██║██║ ╚═╝ ██║         *
*   ╚═╝  ╚═╝ ╚════╝ ╚═╝     ╚═╝╚═╝     ╚═╝         *
*                                                  *
*--------------------------------------------------*
* 文件名: InputBuffer.cs                           *
* 作者  : 哈基咩咩                                  *
* 创建时间: 2025年7月15日                           *
* 功能简介:                                        *
*   通用输入缓冲管理器，支持单一与队列输入缓冲，      *
*   适用于动作、格斗、平台跳跃等需要输入容错的场景。   *
*------------------------------------------------- *
* 特色亮点:                                         *
*   - 支持单例模式，方便全局调用                     *
*   - 支持多种输入类型扩展                          *
*   - 队列缓冲机制，助力连招/复杂输入                *
*   - 可自定义缓冲时间，灵活适配不同需求             *
*   - 断点/清空机制，极致掌控输入流                 *
*------------------------------------------------- *
* 适用场景:                                         *
*   - 角色动作响应优化                              *
*   - 连招/技能输入判定                             *
*   - 容错输入窗口                                  *
****************************************************
*/
using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Events;



[Serializable]
public enum E_InputType
{
    Jump,
    Attack,
}


 
/// <summary>
/// 通用输入缓冲管理器
/// </summary>
public class InputBuffer : MonoBehaviour
{
    //单例模式
    private static InputBuffer _instance;
    [SerializeField]private bool isSingleton = true;
    public static InputBuffer Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new GameObject("InputBuffer").AddComponent<InputBuffer>();
                DontDestroyOnLoad(_instance.gameObject);
            }
            return _instance;
        }
    }

    #region 事件回调

    #endregion
    void Awake()
    {
        if(isSingleton && _instance == null)
        {
            _instance = this;
        }
        InitInputBuffer();
    }
 
   
    /// <summary>
    /// 每帧更新缓冲状态
    /// </summary>
    void Update()
    {
        UpdateBuffer();
        UpdateQueueBuffer();
    }
    void OnDestroy()
    {
        ClearInputBuffer();
        ClearQueueInputBuffer();
    }



    #region 单一输入缓冲

    [Serializable]
    public class BufferSlot
    {
        [SerializeField, LabelText("缓冲时间")] public float bufferTime = 0.2f;
        private float timer = 0f;//计时器
        private bool buffered = false;//缓冲状态
        public bool Buffered { get => buffered; set => buffered = value; }

        public float Timer { get => timer; set => timer = value; }

    }


    [ShowInInspector,DictionaryDrawerSettings(
        KeyLabel = "输入类型", ValueLabel = "缓冲状态",
        DisplayMode = DictionaryDisplayOptions.ExpandedFoldout)]
    public Dictionary<E_InputType,BufferSlot> onceBufferDict = new ();
    
    /// <summary>
    /// 初始化输入缓冲
    /// </summary>
    public void InitInputBuffer() {
        onceBufferDict.Clear();
        onceBufferDict.Add(E_InputType.Jump,new BufferSlot(){bufferTime = 0.2f});
        // queueBufferDict.Add(E_InputType.Attack,new Queue<BufferSlot>{} );
    }

    
    /// <summary>
    /// 缓冲输入
    /// </summary>
    public void AddInputBuffer(E_InputType inputType)
    {
        if(!onceBufferDict.ContainsKey(inputType))
        {
            onceBufferDict.Add(inputType,new BufferSlot());
        }

        if (onceBufferDict.TryGetValue(inputType, out var slot))
         {
            slot.Buffered = true;
            slot.Timer = slot.bufferTime;
        }
    }

   private void UpdateBuffer()
    {
        if(onceBufferDict.Count <= 0 || onceBufferDict == null || onceBufferDict.Values == null)return;

        foreach (var slot in onceBufferDict.Values)
        {
            if (!slot.Buffered) continue;
            
            slot.Timer -= Time.deltaTime;
            if (slot.Timer <= 0)
            {
                slot.Buffered = false;
            }
            
        }
    }

    /// <summary>
    /// 检查并消耗缓冲输入（如在角色满足条件时调用）
    /// </summary>
    public bool ConsumeInputBuffer(E_InputType inputType, UnityAction callback = null)
    {
        if(!onceBufferDict.ContainsKey(inputType))return false;

        if (onceBufferDict.TryGetValue(inputType, out var slot) && slot.Buffered)
        {
            slot.Buffered = false;
            callback?.Invoke();
            return true;
        }
        return false;
    }
    /// <summary>
    /// 清空输入缓冲
    /// </summary>
    public void ClearInputBuffer()
    {
        onceBufferDict.Clear();
    }
    #endregion
 
 
    #region 队列输入缓冲 需要配合输入缓冲窗口机制使用
    [SerializeField,LabelText("队列最大缓存数量"), Min(1)]
    private int queueMaxCount = 3;

    [ShowInInspector,DictionaryDrawerSettings(
    KeyLabel = "输入类型", ValueLabel = "缓冲状态",
    DisplayMode = DictionaryDisplayOptions.ExpandedFoldout)]
    private Dictionary<E_InputType, Queue<BufferSlot>> queueBufferDict = new();
    
    public void AddInputQueue(E_InputType inputType, float bufferTime = 0.2f)
    {
    if (!queueBufferDict.ContainsKey(inputType))
        queueBufferDict[inputType] = new Queue<BufferSlot>();

        //如果队列已满 直接退出即可
        var queue = queueBufferDict[inputType];
        if (queue.Count >= queueMaxCount)
            queue.Dequeue();

        queueBufferDict[inputType].Enqueue(new BufferSlot
        {
            bufferTime = bufferTime,
            Timer = bufferTime,
            Buffered = true
        });
    }

    public bool ConsumeQueueInputBuffer(E_InputType inputType, UnityAction callback = null)
    {
        if (queueBufferDict.TryGetValue(inputType, out var queue) && queue.Count > 0)
        {
            var slot = queue.Peek();
            if (slot.Buffered)
            {
                slot.Buffered = false;
                callback?.Invoke();
                queue.Dequeue();
                return true;
            }
        }
        return false;
    }
    public void UpdateQueueBuffer(){
        foreach (var queue in queueBufferDict.Values)
        {
            while (queue.Count > 0)
            {
                var slot = queue.Peek();
                if (!slot.Buffered)
                {
                    queue.Dequeue();
                    continue;
                }
                slot.Timer -= Time.deltaTime;
                if (slot.Timer <= 0)
                {
                    slot.Buffered = false;
                    queue.Dequeue();
                }
                else
                {
                    break; 
                }
            }      
        }
     }
    private void ClearQueueInputBuffer()
    {
        //清除队列字典
        queueBufferDict.Clear();
    }
    
    #endregion

    #region 打断机制
    /// <summary>
    /// 主动打断输入缓冲
    /// </summary>
    /// <param name="inputType"></param>
    public void Interrupt(E_InputType inputType)
    {
        if (onceBufferDict.TryGetValue(inputType, out var slot))
        {
            slot.Buffered = false;
            slot.Timer = 0f;
        }
    }

    /// <summary>
    /// 主动打断所有输入缓冲
    /// </summary>
    public void InterruptAll()
    {
        foreach (var slot in onceBufferDict.Values)
        {
            slot.Buffered = false;
            slot.Timer = 0f;
        }
    }

    #endregion

 
}