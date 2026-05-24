using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine;

public class PlayerJump : MonoBehaviour
{
    public InputBuffer inputBuffer;
    public float jumpForce = 5f;
    private Rigidbody rb;

    public LayerMask groundLayer;
    public float offset;
    public float radius;

    // 队列缓冲测试相关
    public float attackMoveDistance = 2f;
    public float attackDuration = 1f;
    private Vector3 originalPosition;
    private bool isAttacking = false;
    private float attackTimer = 0f;

    // 输入窗口机制相关
    [Header("攻击输入窗口（按T切换）")]
    public bool attackInputWindow = false;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        originalPosition = transform.position;
    }

    void Update()
    {
        // 跳跃缓冲测试
        if (Input.GetKeyDown(KeyCode.Space))
        {
            inputBuffer.AddInputBuffer(E_InputType.Jump);
        }
        if (IsGrounded() && inputBuffer.ConsumeInputBuffer(E_InputType.Jump, Jump))
        {
            // 跳跃逻辑已在Jump()
        }

        // 输入窗口机制：按T切换攻击输入窗口
        if (Input.GetKeyDown(KeyCode.T))
        {
            attackInputWindow = !attackInputWindow;
            Debug.Log($"攻击输入窗口: {(attackInputWindow ? "开启" : "关闭")}");
        }

        // 队列缓冲测试：只有在窗口期内才允许加入攻击输入
        if (Input.GetKeyDown(KeyCode.J) && attackInputWindow)
        {
            inputBuffer.AddInputQueue(E_InputType.Attack, attackDuration);
            Debug.Log("攻击输入已加入队列缓冲");
        }
        // 如果没有在攻击，尝试消耗队列缓冲
        if (!isAttacking && inputBuffer.ConsumeQueueInputBuffer(E_InputType.Attack, Attack))
        {
            // 攻击逻辑已在Attack()
        }

        // 攻击状态计时与回归
        if (isAttacking)
        {
            attackTimer -= Time.deltaTime;
            if (attackTimer <= 0f)
            {
                transform.position = originalPosition;
                isAttacking = false;
            }
        }
    }

    bool IsGrounded()
    {
        //球形触发器
        Collider[] hits = Physics.OverlapSphere(
        transform.position  + Vector3.up * offset, 
        radius, 
        groundLayer);
        return hits.Length > 0;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position + Vector3.up * offset, radius);
    }

    void Jump()
    {
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, jumpForce, rb.linearVelocity.z);
        Debug.Log("Jump!");
    }

    void Attack()
    {
        if (!isAttacking)
        {
            originalPosition = transform.position;
        }
        transform.position = originalPosition + transform.forward * attackMoveDistance;
        attackTimer = attackDuration;
        isAttacking = true;
        Debug.Log("Attack Triggered!");
    }
}