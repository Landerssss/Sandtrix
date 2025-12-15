using UnityEngine;
using System.Collections.Generic;

public class SandTetris : MonoBehaviour
{
    [Header("Game Config")]
    // 1. 分辨率极大提升，实现“细沙”效果
    public int width = 192;      
    public int height = 384;     
    // 2. 每个逻辑方块由多少个物理像素组成（例如 16x16 的沙粒组成一个方块格）
    public int pieceScale = 16;  
    
    public float fallSpeed = 0.5f;
    public float sandSpeed = 0.01f; // 沙子流动速度

    [Header("UI")]
    public TMPro.TextMeshProUGUI scoreText;

    // 核心数据
    private Color32[] pixelBuffer; // 使用一维数组直接操作像素，性能比Color[,]快10倍
    private Color32[] backBuffer;  // 用于双缓冲计算沙子物理
    private Texture2D texture;
    
    // 游戏状态
    private float timer;
    private float sandTimer;
    private int score = 0;
    private bool gameOver = false;

    // 当前方块
    private int[,] currentPiece;
    private Color32 currentColor;
    // pieceX/Y 是基于 scale 的逻辑坐标，但在渲染时会乘以 scale
    // 这里我们直接存储像素坐标会更灵活，但为了旋转方便，我们存储“逻辑网格坐标”
    // 为了实现视频里那种顺滑感，我们将坐标系改为：
    // piecePos 是以“像素”为单位的坐标
    private int piecePixelX, piecePixelY; 

    // 预定义方块 (保持原始矩阵，渲染时放大)
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

    private readonly Color32[] colors = { 
        new Color32(0, 255, 255, 255), // Cyan
        new Color32(0, 0, 255, 255),   // Blue
        new Color32(255, 127, 0, 255), // Orange
        new Color32(255, 255, 0, 255), // Yellow
        new Color32(0, 255, 0, 255),   // Green
        new Color32(255, 0, 255, 255), // Magenta
        new Color32(255, 0, 0, 255)    // Red
    };

    private Color32 colorBlack = new Color32(0, 0, 0, 255);

    void Start()
    {
        // 初始化纹理
        texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Point; // 保持像素清晰
        GetComponent<Renderer>().material.mainTexture = texture;

        // 初始化缓冲区
        pixelBuffer = new Color32[width * height];
        backBuffer = new Color32[width * height];
        
        ClearGrid();
        SpawnPiece();
    }

    void Update()
    {
        if (gameOver) return;

        HandleInput();

        // 方块下落逻辑
        timer += Time.deltaTime;
        if (timer >= fallSpeed)
        {
            // 每次下落 pieceScale 个像素（即一整格），或者你可以改成 1 以便平滑下落
            // 俄罗斯方块通常是一格一格掉
            if (!Move(0, -pieceScale))
            {
                SolidifyPiece();
                CheckLines();
                SpawnPiece();
            }
            timer = 0;
        }

        // 沙子物理模拟 (独立频率)
        sandTimer += Time.deltaTime;
        if (sandTimer >= sandSpeed)
        {
            SimulateSand();
            sandTimer = 0;
        }

        // 渲染
        DrawScene();
    }

    void HandleInput()
    {
        // 左右移动：每次移动 pieceScale 个像素
        if (Input.GetKeyDown(KeyCode.LeftArrow)) Move(-pieceScale, 0);
        if (Input.GetKeyDown(KeyCode.RightArrow)) Move(pieceScale, 0);
        
        // 旋转
        if (Input.GetKeyDown(KeyCode.UpArrow)) Rotate();
        
        // 加速下落
        if (Input.GetKeyDown(KeyCode.DownArrow)) Move(0, -pieceScale);

        // 2. 快速落下 (Hard Drop)
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))
        {
            HardDrop();
        }
    }

    void HardDrop()
    {
        // 循环下落直到无法移动
        while (Move(0, -1)) { } 
        // 这里用 -1 而不是 -pieceScale 是为了让它紧贴地面，不会悬空
        
        SolidifyPiece();
        CheckLines();
        SpawnPiece();
        timer = 0;
    }

    void SpawnPiece()
    {
        int index = Random.Range(0, shapes.Count);
        currentPiece = shapes[index];
        currentColor = colors[index];
        
        // 居中生成
        piecePixelX = (width / 2) - ((currentPiece.GetLength(1) * pieceScale) / 2);
        piecePixelY = height - 1 - (currentPiece.GetLength(0) * pieceScale);

        if (!IsValidPosition(piecePixelX, piecePixelY, currentPiece))
        {
            gameOver = true;
            Debug.Log("Game Over");
        }
    }

    bool Move(int dx, int dy)
    {
        if (IsValidPosition(piecePixelX + dx, piecePixelY + dy, currentPiece))
        {
            piecePixelX += dx;
            piecePixelY += dy;
            return true;
        }
        return false;
    }

    void Rotate()
    {
        int rows = currentPiece.GetLength(0);
        int cols = currentPiece.GetLength(1);
        int[,] newShape = new int[cols, rows];

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                newShape[c, rows - 1 - r] = currentPiece[r, c];
            }
        }

        // 简单的旋转修正 (Wall Kick)
        if (IsValidPosition(piecePixelX, piecePixelY, newShape))
        {
            currentPiece = newShape;
        }
        else if (IsValidPosition(piecePixelX - pieceScale, piecePixelY, newShape)) // 尝试左踢
        {
            piecePixelX -= pieceScale;
            currentPiece = newShape;
        }
        else if (IsValidPosition(piecePixelX + pieceScale, piecePixelY, newShape)) // 尝试右踢
        {
            piecePixelX += pieceScale;
            currentPiece = newShape;
        }
    }

    // 碰撞检测：检查方块内的每一个“大格子”区域是否碰到沙子
    bool IsValidPosition(int px, int py, int[,] shape)
    {
        int rows = shape.GetLength(0);
        int cols = shape.GetLength(1);

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                if (shape[r, c] != 0)
                {
                    // 检查这个逻辑格对应的 pieceScale x pieceScale 区域
                    int startX = px + c * pieceScale;
                    int startY = py + r * pieceScale;

                    // 只要区域内有任何一个非黑像素，或者出界，就算碰撞
                    for (int y = 0; y < pieceScale; y++)
                    {
                        for (int x = 0; x < pieceScale; x++)
                        {
                            int checkX = startX + x;
                            int checkY = startY + y;

                            if (checkX < 0 || checkX >= width || checkY < 0 || checkY >= height)
                                return false;

                            if (!IsBlack(pixelBuffer[checkY * width + checkX]))
                                return false;
                        }
                    }
                }
            }
        }
        return true;
    }

    // 将方块变为沙子写入 pixelBuffer
    void SolidifyPiece()
    {
        int rows = currentPiece.GetLength(0);
        int cols = currentPiece.GetLength(1);

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                if (currentPiece[r, c] != 0)
                {
                    int startX = piecePixelX + c * pieceScale;
                    int startY = piecePixelY + r * pieceScale;

                    // 填满这个区域
                    for (int y = 0; y < pieceScale; y++)
                    {
                        for (int x = 0; x < pieceScale; x++)
                        {
                            int drawX = startX + x;
                            int drawY = startY + y;
                            if (drawX >= 0 && drawX < width && drawY >= 0 && drawY < height)
                            {
                                pixelBuffer[drawY * width + drawX] = currentColor;
                            }
                        }
                    }
                }
            }
        }
    }

    // 优化的流沙算法
    void SimulateSand()
    {
        bool changed = false;
        // 复制一份当前状态用于读取，写入操作直接在 pixelBuffer 进行
        System.Array.Copy(pixelBuffer, backBuffer, pixelBuffer.Length);

        // 从下往上遍历
        for (int y = 0; y < height - 1; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = y * width + x;
                int idxUp = (y + 1) * width + x;

                Color32 p = backBuffer[idxUp]; // 读取上方像素

                if (!IsBlack(p)) // 如果上面有沙子
                {
                    // 1. 正下方为空
                    if (IsBlack(backBuffer[idx])) 
                    {
                        pixelBuffer[idx] = p;         // 掉下来
                        pixelBuffer[idxUp] = colorBlack; // 原位置清空
                        changed = true;
                    }
                    else 
                    {
                        // 2. 左下方
                        bool canLeft = x > 0 && IsBlack(backBuffer[idx - 1]);
                        // 3. 右下方
                        bool canRight = x < width - 1 && IsBlack(backBuffer[idx + 1]);

                        if (canLeft && canRight)
                        {
                            // 随机向左或向右，增加自然感
                            int offset = Random.Range(0, 2) == 0 ? -1 : 1;
                            pixelBuffer[idx + offset] = p;
                            pixelBuffer[idxUp] = colorBlack;
                            changed = true;
                        }
                        else if (canLeft)
                        {
                            pixelBuffer[idx - 1] = p;
                            pixelBuffer[idxUp] = colorBlack;
                            changed = true;
                        }
                        else if (canRight)
                        {
                            pixelBuffer[idx + 1] = p;
                            pixelBuffer[idxUp] = colorBlack;
                            changed = true;
                        }
                    }
                }
            }
        }
    }

    void CheckLines()
    {
        // 简单的满行消除逻辑：检测一行是否有足够多的非黑像素
        // 视频中的流沙消除通常要求“连接左右两壁”的同色，这里先做简单的“满行消除”
        for (int y = 0; y < height; y++)
        {
            int sandCount = 0;
            for (int x = 0; x < width; x++)
            {
                if (!IsBlack(pixelBuffer[y * width + x])) sandCount++;
            }

            // 如果这一行 95% 都是沙子，就消除
            if (sandCount > width * 0.95f) 
            {
                score += 100;
                UpdateScore();
                // 消除这一行 (变成黑色)
                for (int x = 0; x < width; x++) 
                    pixelBuffer[y * width + x] = colorBlack;
            }
        }
    }

    void DrawScene()
    {
        // 1. 设置沙子数据
        texture.SetPixels32(pixelBuffer);

        // 2. 叠加绘制当前的方块 (直接画在 texture 上，不修改 pixelBuffer 物理数据)
        // 注意：这里需要临时修改 texture，但不能 Apply 到 pixelBuffer 里
        if (currentPiece != null)
        {
            int rows = currentPiece.GetLength(0);
            int cols = currentPiece.GetLength(1);

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (currentPiece[r, c] != 0)
                    {
                        int startX = piecePixelX + c * pieceScale;
                        int startY = piecePixelY + r * pieceScale;

                        // 绘制方块像素块
                        for (int y = 0; y < pieceScale; y++)
                        {
                            for (int x = 0; x < pieceScale; x++)
                            {
                                int drawX = startX + x;
                                int drawY = startY + y;
                                if (drawX >= 0 && drawX < width && drawY >= 0 && drawY < height)
                                {
                                    texture.SetPixel(drawX, drawY, currentColor);
                                }
                            }
                        }
                    }
                }
            }
        }

        texture.Apply();
    }

    void ClearGrid()
    {
        for (int i = 0; i < pixelBuffer.Length; i++) pixelBuffer[i] = colorBlack;
    }

    bool IsBlack(Color32 c)
    {
        return c.r == 0 && c.g == 0 && c.b == 0;
    }

    void UpdateScore()
    {
        if (scoreText != null) scoreText.text = "Score: " + score;
    }
}