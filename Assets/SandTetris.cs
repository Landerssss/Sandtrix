using UnityEngine;
using System.Collections.Generic;

public class SandTetris : MonoBehaviour
{
    [Header("Settings")]
    public int width = 60;   // 游戏逻辑宽度（像素级）
    public int height = 120; // 游戏逻辑高度
    public float fallSpeed = 0.5f; // 方块下落速度
    public float sandSpeed = 0.02f; // 流沙模拟速度

    [Header("UI")]
    public TMPro.TextMeshProUGUI scoreText; // 需要在场景里建一个 TextMeshPro 拖进来

    // 核心数据
    private Color[,] grid;
    private Texture2D texture;
    private float timer;
    private float sandTimer;
    private int score = 0;
    private bool gameOver = false;

    // 当前方块数据
    private int[,] currentPiece;
    private Color currentColor;
    private int pieceX, pieceY;

    // 预定义 7 种方块 (I, J, L, O, S, T, Z)
    private readonly List<int[,]> shapes = new List<int[,]>()
    {
        new int[,] { {1,1,1,1} }, // I
        new int[,] { {1,0,0}, {1,1,1} }, // J
        new int[,] { {0,0,1}, {1,1,1} }, // L
        new int[,] { {1,1}, {1,1} }, // O
        new int[,] { {0,1,1}, {1,1,0} }, // S
        new int[,] { {0,1,0}, {1,1,1} }, // T
        new int[,] { {1,1,0}, {0,1,1} }  // Z
    };

    private readonly Color[] colors = { Color.cyan, Color.blue, new Color(1f, 0.5f, 0f), Color.yellow, Color.green, Color.magenta, Color.red };

    void Start()
    {
        // 1. 初始化纹理和网格
        grid = new Color[width, height];
        texture = new Texture2D(width, height);
        texture.filterMode = FilterMode.Point; // 像素风格，不模糊
        GetComponent<Renderer>().material.mainTexture = texture;

        // 初始化网格颜色为黑色（空）
        ClearGrid();
        
        // 2. 生成第一个方块
        SpawnPiece();
    }

    void Update()
    {
        if (gameOver) return;

        // --- 输入控制 ---
        if (Input.GetKeyDown(KeyCode.LeftArrow)) Move(-1, 0);
        if (Input.GetKeyDown(KeyCode.RightArrow)) Move(1, 0);
        if (Input.GetKeyDown(KeyCode.UpArrow)) Rotate();
        if (Input.GetKeyDown(KeyCode.DownArrow)) Move(0, -1); // 加速下落

        // --- 方块自动下落逻辑 ---
        timer += Time.deltaTime;
        if (timer >= fallSpeed)
        {
            if (!Move(0, -1))
            {
                // 如果无法下落，固定方块变成沙子
                SolidifyPiece();
                CheckLines(); // 检查是否有满行
                SpawnPiece(); // 生成新方块
            }
            timer = 0;
        }

        // --- 流沙物理模拟 ---
        sandTimer += Time.deltaTime;
        if (sandTimer >= sandSpeed)
        {
            SimulateSand();
            sandTimer = 0;
        }

        // --- 渲染 ---
        ApplyGridToTexture();
    }

    // 生成新方块
    void SpawnPiece()
    {
        int index = Random.Range(0, shapes.Count);
        currentPiece = shapes[index];
        currentColor = colors[index];
        
        pieceX = width / 2 - currentPiece.GetLength(1) / 2;
        pieceY = height - 1 - currentPiece.GetLength(0);

        // 如果生成位置就被卡住，游戏结束
        if (!IsValidPosition(pieceX, pieceY, currentPiece))
        {
            gameOver = true;
            Debug.Log("Game Over!");
        }
    }

    // 移动逻辑 (返回 true 表示移动成功)
    bool Move(int dx, int dy)
    {
        if (IsValidPosition(pieceX + dx, pieceY + dy, currentPiece))
        {
            pieceX += dx;
            pieceY += dy;
            return true;
        }
        return false;
    }

    // 旋转逻辑
    void Rotate()
    {
        int rows = currentPiece.GetLength(0);
        int cols = currentPiece.GetLength(1);
        int[,] newShape = new int[cols, rows];

        // 矩阵旋转 90 度
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                newShape[c, rows - 1 - r] = currentPiece[r, c];
            }
        }

        if (IsValidPosition(pieceX, pieceY, newShape))
        {
            currentPiece = newShape;
        }
        // 尝试简单的踢墙 (Wall Kick)
        else if (IsValidPosition(pieceX - 1, pieceY, newShape)) pieceX -= 1;
        else if (IsValidPosition(pieceX + 1, pieceY, newShape)) pieceX += 1;
    }

    // 碰撞检测
    bool IsValidPosition(int px, int py, int[,] shape)
    {
        for (int r = 0; r < shape.GetLength(0); r++)
        {
            for (int c = 0; c < shape.GetLength(1); c++)
            {
                if (shape[r, c] != 0)
                {
                    int targetX = px + c;
                    int targetY = py + r;

                    // 边界检查
                    if (targetX < 0 || targetX >= width || targetY < 0 || targetY >= height)
                        return false;

                    // 与已存在的沙子碰撞检查
                    if (grid[targetX, targetY] != Color.black)
                        return false;
                }
            }
        }
        return true;
    }

    // 将方块转化为沙子
    void SolidifyPiece()
    {
        for (int r = 0; r < currentPiece.GetLength(0); r++)
        {
            for (int c = 0; c < currentPiece.GetLength(1); c++)
            {
                if (currentPiece[r, c] != 0)
                {
                    grid[pieceX + c, pieceY + r] = currentColor;
                }
            }
        }
    }

    // 流沙核心算法 (从下往上遍历)
    void SimulateSand()
    {
        bool changed = false;
        // 注意：必须从下往上遍历 (y=0 到 height-1)，否则一个像素一帧内会直接掉到底
        for (int y = 0; y < height - 1; y++) 
        {
            for (int x = 0; x < width; x++)
            {
                Color c = grid[x, y + 1]; // 看上面的像素
                if (c != Color.black) // 如果上面有沙子
                {
                    // 1. 正下方是空的？掉下去
                    if (grid[x, y] == Color.black)
                    {
                        grid[x, y] = c;
                        grid[x, y + 1] = Color.black;
                        changed = true;
                    }
                    // 2. 下方堵住，左下角是空的？流向左下
                    else if (x > 0 && grid[x - 1, y] == Color.black)
                    {
                        grid[x - 1, y] = c;
                        grid[x, y + 1] = Color.black;
                        changed = true;
                    }
                    // 3. 下方堵住，右下角是空的？流向右下
                    else if (x < width - 1 && grid[x + 1, y] == Color.black)
                    {
                        grid[x + 1, y] = c;
                        grid[x, y + 1] = Color.black;
                        changed = true;
                    }
                }
            }
        }
    }

    // 检查是否有颜色填满一行（流沙版的消除规则通常是：只要一行连通的颜色达成条件，这里简化为一行几乎满了就消）
    void CheckLines()
    {
        // 简化规则：如果一行被非黑色填满，则消除
        // 为了增加流沙的宽容度，我们可以设定阈值，比如 90% 满
        // 这里使用经典规则：全满
        
        for (int y = 0; y < height; y++)
        {
            bool full = true;
            for (int x = 0; x < width; x++)
            {
                if (grid[x, y] == Color.black)
                {
                    full = false;
                    break;
                }
            }

            if (full)
            {
                // 消除这一行
                for (int x = 0; x < width; x++) grid[x, y] = Color.black;
                score += 100;
                UpdateScore();
                // 消除后上面的沙子会自动在 SimulateSand 中掉下来，不需要手动搬运数组
            }
        }
    }

    // 渲染逻辑：把 grid 和 currentPiece 画到 texture 上
    void ApplyGridToTexture()
    {
        // 1. 绘制背景和已固定的沙子
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                texture.SetPixel(x, y, grid[x, y]);
            }
        }

        // 2. 绘制当前正在下落的方块
        if (currentPiece != null)
        {
            for (int r = 0; r < currentPiece.GetLength(0); r++)
            {
                for (int c = 0; c < currentPiece.GetLength(1); c++)
                {
                    if (currentPiece[r, c] != 0)
                    {
                        int drawX = pieceX + c;
                        int drawY = pieceY + r;
                        if (drawX >= 0 && drawX < width && drawY >= 0 && drawY < height)
                        {
                            texture.SetPixel(drawX, drawY, currentColor);
                        }
                    }
                }
            }
        }

        texture.Apply();
    }

    void ClearGrid()
    {
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                grid[x, y] = Color.black;
    }

    void UpdateScore()
    {
        if(scoreText != null) scoreText.text = "Score: " + score;
    }
}