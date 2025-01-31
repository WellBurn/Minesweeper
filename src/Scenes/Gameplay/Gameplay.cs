using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Minesweeper.Constants;
using Minesweeper.Scenes.Gameplay;

public class Gameplay : Control
{
    private enum GameState
    {
        Ready,
        Playing,
        GameOver,
    }

    [Export] public PackedScene CellScene;

    private GameState _gameState = GameState.Ready;
    private int _gridWidth;
    private int _gridHeight;
    private int _numberOfMines;
    private int _remainingLives;

    private Cell[,] _board;

    private ProgressBar _progressBar;
    private ElapsedTime _elapsedTime;
    private RemainingLives _remainingLivesLabel;

    private AudioStreamPlayer _sndMineLoseLife;
    private AudioStreamPlayer _sndMineGameOver;
    private AudioStreamPlayer _sndWin;

    public override void _Ready()
    {
        var settings = Settings.GetInstance();
        _gridWidth = settings.GridWidth;
        _gridHeight = settings.GridHeight;
        _numberOfMines = settings.NumberOfMines;
        _remainingLives = settings.NumberOfLives;

        var scale = settings.Scale;
        var gridContainer = new GridContainer();
        gridContainer.RectScale = new Vector2(scale, scale);

        gridContainer.Columns = _gridHeight;
        Vector2 textureSize = Vector2.Zero;

        var cells = new Cell[_gridWidth, _gridHeight];

        for (int x = 0; x < _gridWidth; x++)
        {
            for (int y = 0; y < _gridHeight; y++)
            {
                var cell = CellScene.Instance<Cell>();
                cell.GridPosition = new Vector2(x, y);
                cell.Connect("gui_input", this, nameof(OnCellPressed), new Godot.Collections.Array(cell));
                textureSize = cell.TextureNormal.GetSize();
                cells[x, y] = cell;
                gridContainer.AddChild(cell);
            }
        }

        _board = cells;

        // center grid container
        gridContainer.AnchorLeft = 0.5f;
        gridContainer.AnchorRight = 0.5f;
        gridContainer.AnchorTop = 0.5f;
        gridContainer.AnchorBottom = 0.5f;

        var totalTextureSize = new Vector2(textureSize.x * _gridHeight * scale, textureSize.y * _gridWidth * scale);

        gridContainer.MarginLeft = -totalTextureSize.x / 2;
        gridContainer.MarginRight = totalTextureSize.x / 2;
        gridContainer.MarginTop = -totalTextureSize.y / 2;
        gridContainer.MarginBottom = totalTextureSize.y / 2;

        AddChild(gridContainer);

        _progressBar = GetNode<ProgressBar>("ProgressBar");
        _progressBar.MinValue = 0;
        _progressBar.Value = 0;

        _elapsedTime = GetNode<ElapsedTime>("ElapsedTime");
        _remainingLivesLabel = GetNode<RemainingLives>("RemainingLives");
        _remainingLivesLabel.UpdateRemainingLives(_remainingLives);

        _sndMineLoseLife = GetNode<AudioStreamPlayer>("SoundMineLoseLife");
        _sndMineGameOver = GetNode<AudioStreamPlayer>("SoundMineGameOver");
        _sndWin = GetNode<AudioStreamPlayer>("SoundWin");
    }

    private void InitBoard(Vector2 exclude)
    {
        int CountNeighboringMines(int x, int y, bool[,] knownMineLocations)
        {
            var mineCount = 0;

            var coordsToCheck = GetValidNeighborCoords(x, y);

            foreach (var (cx, cy) in coordsToCheck)
            {
                if (knownMineLocations[cx, cy])
                {
                    mineCount++;
                }
            }

            return mineCount;
        }

        // pass 1: set mines
        var uniqueMines = new HashSet<Vector2>();
        var rng = new Random();

        while (uniqueMines.Count < _numberOfMines)
        {
            var x = rng.Next(0, _gridWidth);
            var y = rng.Next(0, _gridHeight);

            var mine = new Vector2(x, y);

            if (mine == exclude)
            {
                continue;
            }

            if (uniqueMines.Contains(mine))
            {
                continue;
            }

            uniqueMines.Add(mine);
        }

        var mineLocations = new bool[_gridWidth, _gridHeight];
        var uniqueMineList = uniqueMines.ToList();

        foreach (var mine in uniqueMineList)
        {
            mineLocations[(int)mine.x, (int)mine.y] = true;
        }

        // pass 2: set neighbors
        for (int x = 0; x < _gridWidth; x++)
        {
            for (int y = 0; y < _gridHeight; y++)
            {
                if (mineLocations[x, y])
                {
                    _board[x, y].SetType(CellType.Mine);
                }
                else
                {
                    var count = CountNeighboringMines(x, y, mineLocations);

                    if (count == 0) { _board[x, y].SetType(CellType.Safe0); }
                    if (count == 1) { _board[x, y].SetType(CellType.Safe1); }
                    if (count == 2) { _board[x, y].SetType(CellType.Safe2); }
                    if (count == 3) { _board[x, y].SetType(CellType.Safe3); }
                    if (count == 4) { _board[x, y].SetType(CellType.Safe4); }
                    if (count == 5) { _board[x, y].SetType(CellType.Safe5); }
                    if (count == 6) { _board[x, y].SetType(CellType.Safe6); }
                    if (count == 7) { _board[x, y].SetType(CellType.Safe7); }
                    if (count == 8) { _board[x, y].SetType(CellType.Safe8); }
                }
            }
        }

        _progressBar.MaxValue = GetNumberOfSafeCells();
        _elapsedTime.Start();
    }

    private void OnCellPressed(InputEvent evt, Cell cell)
    {
        if (evt is InputEventMouseButton)
        {
            var mouseEvent = (InputEventMouseButton)evt;
            var buttonIndex = mouseEvent.ButtonIndex;

            if (buttonIndex == 1 && mouseEvent.Pressed)
            {
                PrimaryAction(cell);
            }

            if (buttonIndex == 2 && mouseEvent.Pressed)
            {
                SecondaryAction(cell);
            }
        }
    }

    private void PrimaryAction(Cell cell)
    {
        if (_gameState == GameState.GameOver)
        {
            return;
        }

        if (cell.Flagged)
        {
            return;
        }

        void RevealNeighboringZeros(int x, int y)
        {
            var coordsToCheck = GetValidNeighborCoords(x, y);

            foreach (var (cx, cy) in coordsToCheck)
            {
                var cellToCheck = _board[cx, cy];
                if (!cellToCheck.Revealed)
                {
                    cellToCheck.Reveal();

                    if (cellToCheck.Type == CellType.Safe0)
                    {
                        RevealNeighboringZeros(cx, cy);
                    }
                }
            }
        }

        if (_gameState == GameState.Ready)
        {
            InitBoard(cell.GridPosition);
            _gameState = GameState.Playing;
        }

        var position = cell.GridPosition.ToIntVector();

        if (cell.Type == CellType.Safe0)
        {
            RevealNeighboringZeros(position.Item1, position.Item2);
        }
        else if (cell.Type == CellType.Mine)
        {
            _remainingLives--;
            _remainingLivesLabel.UpdateRemainingLives(_remainingLives);

            if (_remainingLives == 0)
            {
                Lose();
            }
            else
            {
                PlaySound(_sndMineLoseLife);
                cell.SetType(CellType.MineLife);
            }
        }

        cell.Reveal();

        UpdateProgressBar();
        CheckForVictory();
    }

    private void SecondaryAction(Cell cell)
    {
        if (_gameState == GameState.GameOver)
        {
            return;
        }

        cell.ToggleFlag();
    }

    private Tuple<int, int>[] GetValidNeighborCoords(int x, int y)
    {
        bool IsCoordinateValid(Tuple<int, int> coordinates)
        {
            var cx = coordinates.Item1;
            var cy = coordinates.Item2;

            // skip if cells are out of bounds
            if (cx < 0 || cx >= _gridWidth) { return false; }
            if (cy < 0 || cy >= _gridHeight) { return false; }

            return true;
        }

        return new[]
        {
            new Tuple<int, int>(x - 1, y - 1),  // top left
            new Tuple<int, int>(x    , y - 1),  // top
            new Tuple<int, int>(x + 1, y - 1),  // top right
            new Tuple<int, int>(x - 1, y),      // left
            new Tuple<int, int>(x + 1, y),      // right
            new Tuple<int, int>(x - 1, y + 1),  // bottom left
            new Tuple<int, int>(x    , y + 1),  // bottom
            new Tuple<int, int>(x + 1, y + 1),  // bottom right
        }
            .Where(IsCoordinateValid)
            .ToArray();
    }

    public void CheckForVictory()
    {
        var total = GetNumberOfSafeCells();
        var revealed = GetNumberOfRevealedCells();

        if (total == revealed)
        {
            Win();
        }
    }

    public void Win()
    {
        _gameState = GameState.GameOver;
        var resetButton = GetNode<ResetButton>("ResetButton");
        resetButton.SetWinTexture();
        RevealMines();
        PlaySound(_sndWin);
        _elapsedTime.Stop();
    }

    public void Lose()
    {
        _gameState = GameState.GameOver;
        var resetButton = GetNode<ResetButton>("ResetButton");
        resetButton.SetLoseTexture();
        RevealMines();
        PlaySound(_sndMineGameOver);
        _elapsedTime.Stop();
    }

    private void RevealMines()
    {
        foreach (var cell in _board)
        {
            cell.RevealIfMine();
        }
    }

    private int GetNumberOfSafeCells()
    {
        return _board.Cast<Cell>().Count(cell => cell.Type != CellType.Mine);
    }

    private int GetNumberOfRevealedCells()
    {
        return _board.Cast<Cell>().Count(cell => cell.Type != CellType.Mine && cell.Revealed);
    }

    private void UpdateProgressBar()
    {
        _progressBar.Value = GetNumberOfRevealedCells();
    }

    private void PlaySound(AudioStreamPlayer audio)
    {
        if (Settings.GetInstance().SoundEnabled)
        {
            audio.Play();
        }
    }

    private void _on_TextureButton_pressed()
    {
        GetTree().ReloadCurrentScene();
    }
}
