using UnityEngine;
using UnityEngine.InputSystem;

public class HorseController : MonoBehaviour
{
    [Header("移動設定")]
    public float baseSpeed = 5f;
    public float multiplier = 3f;
    public float turnSpeed = 100f;

    [Header("騎乘設定")]
    public GameObject player;          // 玩家物件 PlayerRig
    public Transform saddlePoint;      // 馬鞍上的騎乘位置
    public float mountDistance = 2.5f; // 允許騎乘距離

    private bool isMounted = false;
    private CharacterController controller;
    private Animator animator;

    // =========================================================
    // Start
    // =========================================================
    void Start()
    {
        // -----------------------------------------------------
        // 馬自己的 CharacterController
        // -----------------------------------------------------
        controller = GetComponent<CharacterController>();

        if (controller == null)
        {
            controller = gameObject.AddComponent<CharacterController>();

            Debug.Log(
                "【馬匹初始化】沒有 CharacterController，已自動建立。"
            );
        }

        // -----------------------------------------------------
        // 取得 Animator
        // -----------------------------------------------------
        animator = GetComponentInChildren<Animator>();

        if (animator != null &&
            animator.runtimeAnimatorController == null)
        {
            Debug.LogWarning(
                "【馬匹警告】Animator 沒有指定 Controller，" +
                "暫時停用動畫控制。"
            );

            animator = null;
        }

        // -----------------------------------------------------
        // 如果 Inspector 沒有指定 Player
        // 嘗試尋找 Tag = Player
        // -----------------------------------------------------
        if (player == null)
        {
            player = GameObject.FindWithTag("Player");
        }

        if (player == null)
        {
            Debug.LogError(
                "【馬匹錯誤】找不到 Player！\n" +
                "請確認 PlayerRig 的 Tag = Player，" +
                "或直接把 PlayerRig 拖到 HorseController 的 Player 欄位。"
            );
        }
        else
        {
            Debug.Log(
                "【馬匹初始化】找到玩家：" +
                player.name
            );
        }

        // -----------------------------------------------------
        // 檢查 SaddlePoint
        // -----------------------------------------------------
        if (saddlePoint == null)
        {
            Debug.LogWarning(
                "【馬匹警告】Saddle Point 尚未指定！\n" +
                "請建立 SaddlePoint，並拖到 HorseController 的 " +
                "Saddle Point 欄位。"
            );
        }
        else
        {
            Debug.Log(
                "【馬匹初始化】SaddlePoint：" +
                saddlePoint.name
            );
        }
    }

    // =========================================================
    // Update
    // =========================================================
    void Update()
    {
        // -----------------------------------------------------
        // E：騎乘 / 下馬
        // -----------------------------------------------------
        if (Keyboard.current != null &&
            Keyboard.current.eKey.wasPressedThisFrame)
        {
            if (isMounted)
            {
                Dismount();
            }
            else
            {
                TryMount();
            }
        }

        // -----------------------------------------------------
        // 騎乘狀態下控制馬
        // -----------------------------------------------------
        if (isMounted)
        {
            HandleMovement();
        }
    }

    // =========================================================
    // 嘗試騎乘
    // =========================================================
    void TryMount()
    {
        // ==================================================
        // 1. 確認 Player
        // ==================================================
        if (player == null)
        {
            player = GameObject.FindWithTag("Player");
        }

        if (player == null)
        {
            Debug.LogError(
                "【騎乘失敗】找不到 Player！\n" +
                "請確認 PlayerRig 的 Tag = Player。"
            );

            return;
        }

        // ==================================================
        // 2. 確認 SaddlePoint
        // ==================================================
        if (saddlePoint == null)
        {
            Debug.LogError(
                "【騎乘失敗】沒有指定 SaddlePoint！"
            );

            return;
        }

        // ==================================================
        // 3. 不再使用 PlayerRig Root 計算距離
        // ==================================================
        Debug.Log(
            "【騎乘】找到玩家：" +
            player.name
        );

        Debug.Log(
            "【騎乘】騎乘點：" +
            saddlePoint.name
        );

        // ==================================================
        // 4. 開始騎乘
        // ==================================================
        isMounted = true;

        // ==================================================
        // 5. 停用玩家 CharacterController
        // ==================================================
        CharacterController playerController =
            player.GetComponent<CharacterController>();

        if (playerController != null)
        {
            playerController.enabled = false;

            Debug.Log(
                "【騎乘】玩家 CharacterController 已停用。"
            );
        }

        // ==================================================
        // 6. 將玩家掛到 SaddlePoint
        // ==================================================
        player.transform.SetParent(
            saddlePoint,
            false
        );

        // ==================================================
        // 7. 玩家位置對齊 SaddlePoint
        // ==================================================
        player.transform.localPosition =
            Vector3.zero;

        player.transform.localRotation =
            Quaternion.identity;

        // ==================================================
        // 8. 騎乘成功
        // ==================================================
        Debug.Log(
            "========================================"
        );

        Debug.Log(
            "【騎乘成功！】"
        );

        Debug.Log(
            "玩家：" +
            player.name
        );

        Debug.Log(
            "SaddlePoint：" +
            saddlePoint.name
        );

        Debug.Log(
            "========================================"
        );
    }

    // =========================================================
    // 下馬
    // =========================================================
    void Dismount()
    {
        // -----------------------------------------------------
        // 修改騎乘狀態
        // -----------------------------------------------------
        isMounted = false;

        if (player == null)
        {
            Debug.LogWarning(
                "【下馬失敗】找不到 Player。"
            );

            return;
        }

        // -----------------------------------------------------
        // 解除 Parent
        // -----------------------------------------------------
        player.transform.SetParent(null);

        // -----------------------------------------------------
        // 把玩家放到馬的右側
        // -----------------------------------------------------
        player.transform.position =
            transform.position +
            transform.right * 1.5f;

        // -----------------------------------------------------
        // 重新啟用玩家 CharacterController
        // -----------------------------------------------------
        CharacterController playerController =
            player.GetComponent<CharacterController>();

        if (playerController != null)
        {
            playerController.enabled = true;
        }

        Debug.Log(
            "【已下馬】玩家已離開馬匹。"
        );
    }

    // =========================================================
    // 騎乘時馬匹移動
    // =========================================================
    void HandleMovement()
    {
        float horizontal = 0f;
        float vertical = 0f;

        // -----------------------------------------------------
        // WASD
        // -----------------------------------------------------
        if (Keyboard.current != null)
        {
            if (Keyboard.current.aKey.isPressed)
            {
                horizontal -= 1f;
            }

            if (Keyboard.current.dKey.isPressed)
            {
                horizontal += 1f;
            }

            if (Keyboard.current.wKey.isPressed)
            {
                vertical += 1f;
            }

            if (Keyboard.current.sKey.isPressed)
            {
                vertical -= 1f;
            }
        }

        // -----------------------------------------------------
        // 左右旋轉
        // -----------------------------------------------------
        transform.Rotate(
            0f,
            horizontal * turnSpeed * Time.deltaTime,
            0f
        );

        // -----------------------------------------------------
        // 移動速度
        // -----------------------------------------------------
        float currentSpeed =
            baseSpeed * multiplier;

        Vector3 move =
            transform.forward *
            vertical *
            currentSpeed;

        // -----------------------------------------------------
        // 重力
        // -----------------------------------------------------
        if (!controller.isGrounded)
        {
            move.y -= 9.81f;
        }

        // -----------------------------------------------------
        // 移動馬匹
        // -----------------------------------------------------
        controller.Move(
            move * Time.deltaTime
        );

        // -----------------------------------------------------
        // Animator
        // -----------------------------------------------------
        if (animator != null &&
            animator.runtimeAnimatorController != null)
        {
            animator.SetFloat(
                "Speed",
                Mathf.Abs(vertical)
            );
        }
    }
}