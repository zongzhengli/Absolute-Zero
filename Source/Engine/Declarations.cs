﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

namespace AbsoluteZero {

    /// <summary>
    /// The declarations component of the Absolute Zero chess engine. 
    /// </summary>
    partial class Zero {

        // Miscellaneous constants. 
        public static readonly String Version = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion;
        public Boolean NewFeature = false;

        // Formatting constants. 
        private const Int32 SingleVariationDepth = 5;
        private const Int32 DepthWidth = 8;
        private const Int32 ValueWidth = 9;

        // Search constants. 
        public const Int32 DepthLimit = 64;
        public const Int32 PlyLimit = DepthLimit + 64;
        public const Int32 MovesLimit = 256;
        public const Int32 HashAllocation = 64;
        public const Int32 TimeResolution = 1000;

        public Int32 AspirationWindow = 17;
        public Int32 NullMoveReduction = 3;
        public Int32 NullMoveAggressiveDepth = 7;
        public Int32 NullMoveAggressiveDivisor = 5;
        public Int32 LateMoveReduction = 2;
        public Single HashMoveValue = 60F;
        public Int32 KillerMovesAllocation = 2;
        public Single KillerMoveValue = .9F;
        public Single KillerMoveSlotValue = -.01F;
        public Single QueenPromotionMoveValue = 1F;
        public Int32[] FutilityMargin = { 0, 104, 125, 250, 271, 375 };

        private const Int32 Contempt = 30;
        private const Int32 DrawValue = -Contempt;
        private const Int32 CheckmateValue = 10000;
        private const Int32 NearCheckmateValue = CheckmateValue - PlyLimit;
        private const Int32 Infinity = 9999999;

        private const Double TimeControlsExpectedLatency = 50;
        private const Double TimeControlsContinuationThreshold = .7;
        private const Double TimeControlsResearchThreshold = .5;
        private const Double TimeControlsResearchExtension = .7;
        private const Int32 TimeControlsLossResolution = 10;
        private const Double TimeControlsLossThreshold = .8;
        private static readonly Double[] TimeControlsLossExtension = { 0, .1, .2, .4, .8, .9, 1, 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 1.9, 2, 2.1, 2.4 };

        // Evaluation constants. 
        public Int32 KingOnOpenFileValue = -58;
        public Int32 KingAdjacentToOpenFileValue = -42;

        public Int32[][] QueenToEnemyKingSpatialValue = new Int32[64][];
        public Int32[] QueenDistanceToEnemyKingValue_ = { 0, 17, 8, 4, 0, -4, -8, -12 };

        public Int32 BishopPairValue = 29;
        public Int32[] BishopMobilityValue = { -25, -12, -3, 0, 2, 5, 8, 10, 12, 13, 15, 17, 18, 18 };

        public Int32[][] KnightToEnemyKingSpatialValue = new Int32[64][];
        public Int32[] KnightDistanceToEnemyKingValue_ = { 0, 8, 8, 6, 4, 0, -4, -6, -8, -10, -12, -13, -15, -17, -25 };
        public Int32[] KnightMovesToEnemyKingValue_ = { 0, 21, 8, 0, -4, -8, -12 };
        public Int32[] KnightMobilityValue = { -21, -8, -2, 0, 2, 5, 8, 10, 12 };

        public Int32 PawnEndgameGainValue = 17;
        public Int32 PawnNearKingValue = 14;
        public Int32 DoubledPawnValue = -21;
        public Int32 IsolatedPawnValue = -17;
        public Int32 PassedPawnValue = 25;
        public Int32 PawnAttackValue = 17;
        public Int32 PawnDefenceValue = 6;
        public Int32 PawnDeficiencyValue = -29;

        public static readonly Int32[] PieceValue = new Int32[14];
        public Int32 TempoValue = 6;

        private static readonly UInt64[] PawnShieldBitboard = new UInt64[64];
        private static readonly UInt64[] ShortAdjacentFilesBitboard = new UInt64[64];
        private static readonly UInt64[][] PawnBlockadeBitboard = { new UInt64[64], new UInt64[64] };
        private static readonly UInt64[][] ShortForwardFileBitboard = { new UInt64[64], new UInt64[64] };
        private const UInt64 NotAFileBitboard = 18374403900871474942UL;
        private const UInt64 NotHFileBitboard = 9187201950435737471UL;

        private static readonly Int32[][] RectilinearDistance = new Int32[64][];
        private static readonly Int32[][] ChebyshevDistance = new Int32[64][];
        private static readonly Int32[][] KnightMoveDistance = new Int32[64][];
        private static readonly Single PhaseCoefficient;

        // Search variables. 
        private HashTable table = new HashTable(HashAllocation);
        private Int32[][] generatedMoves = new Int32[PlyLimit][];
        private Int32[][] pvMoves = new Int32[PlyLimit][];
        private Int32[] pvLength = new Int32[PlyLimit];
        private Int32[][] killerMoves = new Int32[PlyLimit][];
        private Single[] moveValues = new Single[MovesLimit];
        private List<Int32> pv = new List<Int32>();
        private Stopwatch stopwatch = new Stopwatch();
        private Boolean abortSearch;
        private Double timeLimit;
        private Double timeExtension;
        private Double timeExtensionLimit;
        private Int32 finalAlpha = 0;
        private Int32 rootAlpha = 0;
        private Int64 totalNodes;
        private Int64 quiescenceNodes;
        private Int32 referenceNodes;
        private Int64 hashProbes;
        private Int64 hashCutoffs;

        // Evaluation variables. 
        private static readonly UInt64[] minorAttackBitboard = new UInt64[2];
        private static readonly UInt64[] pawnAttackBitboard = new UInt64[2];
        private static readonly Int32[] kingSquare = new Int32[2];

        static Zero() {

            // Initialize piece values. 
            PieceValue[Piece.White | Piece.King] = 3000;
            PieceValue[Piece.White | Piece.Queen] = 1025;
            PieceValue[Piece.White | Piece.Rook] = 575;
            PieceValue[Piece.White | Piece.Bishop] = 370;
            PieceValue[Piece.White | Piece.Knight] = 350;
            PieceValue[Piece.White | Piece.Pawn] = 100;
            for (Int32 piece = Piece.Min; piece <= Piece.Max; piece += 2)
                PieceValue[Piece.Black | piece] = -PieceValue[Piece.White | piece];
            PieceValue[Piece.Empty] = 0;

            PhaseCoefficient += PieceValue[Piece.Queen];
            PhaseCoefficient += 2 * PieceValue[Piece.Rook];
            PhaseCoefficient += 2 * PieceValue[Piece.Bishop];
            PhaseCoefficient += 2 * PieceValue[Piece.Knight];
            PhaseCoefficient += 8 * PieceValue[Piece.Pawn];
            PhaseCoefficient = 1 / PhaseCoefficient;

            for (Int32 square = 0; square < 64; square++) {

                // Initialize piece square tables. 
                Int32 reflected = Position.File(square) + (7 - Position.Rank(square)) * 8;
                KingOpeningPositionValue[Piece.Black][square] = -KingOpeningPositionValue[Piece.White][reflected];
                KingEndgamePositionValue[Piece.Black][square] = -KingEndgamePositionValue[Piece.White][reflected];
                QueenOpeningPositionValue[Piece.Black][square] = -QueenOpeningPositionValue[Piece.White][reflected];
                RookPositionValue[Piece.Black][square] = -RookPositionValue[Piece.White][reflected];
                BishopPositionValue[Piece.Black][square] = -BishopPositionValue[Piece.White][reflected];
                KnightOpeningPositionValue[Piece.Black][square] = -KnightOpeningPositionValue[Piece.White][reflected];
                PawnPositionValue[Piece.Black][square] = -PawnPositionValue[Piece.White][reflected];
                PassedPawnEndgamePositionValue[Piece.Black][square] = -PassedPawnEndgamePositionValue[Piece.White][reflected];

                // Initialize pawn shield bitboard table. 
                PawnShieldBitboard[square] = Bit.File[square];
                if (Position.File(square) > 0)
                    PawnShieldBitboard[square] |= Bit.File[square - 1];
                if (Position.File(square) < 7)
                    PawnShieldBitboard[square] |= Bit.File[square + 1];
                PawnShieldBitboard[square] &= Bit.FloodFill(square, 2);

                // Initialize short adjacent files bitboard table. 
                if (Position.File(square) > 0)
                    ShortAdjacentFilesBitboard[square] |= Bit.File[square - 1] & Bit.FloodFill(square - 1, 3);
                if (Position.File(square) < 7)
                    ShortAdjacentFilesBitboard[square] |= Bit.File[square + 1] & Bit.FloodFill(square + 1, 3);

                // Initialize pawn blockade bitboard table. 
                PawnBlockadeBitboard[Piece.White][square] = Attack.RayN[square];
                if (Position.File(square) > 0)
                    PawnBlockadeBitboard[Piece.White][square] |= Attack.RayN[square - 1];
                if (Position.File(square) < 7)
                    PawnBlockadeBitboard[Piece.White][square] |= Attack.RayN[square + 1];
                PawnBlockadeBitboard[Piece.Black][square] = Attack.RayS[square];
                if (Position.File(square) > 0)
                    PawnBlockadeBitboard[Piece.Black][square] |= Attack.RayS[square - 1];
                if (Position.File(square) < 7)
                    PawnBlockadeBitboard[Piece.Black][square] |= Attack.RayS[square + 1];

                // Initialize short forward file bitboard table.
                ShortForwardFileBitboard[Piece.White][square] = Attack.RayN[square] & Bit.FloodFill(square, 3);
                ShortForwardFileBitboard[Piece.Black][square] = Attack.RayS[square] & Bit.FloodFill(square, 3);

                // Initialize rectilinear distance table.
                RectilinearDistance[square] = new Int32[64];
                for (Int32 to = 0; to < 64; to++)
                    RectilinearDistance[square][to] = Math.Abs(Position.File(square) - Position.File(to)) + Math.Abs(Position.Rank(square) - Position.Rank(to));

                // Initialize chebyshev distance table. 
                ChebyshevDistance[square] = new Int32[64];
                for (Int32 to = 0; to < 64; to++)
                    ChebyshevDistance[square][to] = Math.Max(Math.Abs(Position.File(square) - Position.File(to)), Math.Abs(Position.Rank(square) - Position.Rank(to)));

                // Initialize knight move distance table. 
                KnightMoveDistance[square] = new Int32[64];
                for (Int32 i = 0; i < KnightMoveDistance[square].Length; i++)
                    KnightMoveDistance[square][i] = 6;
                for (Int32 moves = 1; moves <= 5; moves++) {
                    UInt64 moveBitboard = Attack.KnightFill(square, moves);
                    for (Int32 to = 0; to < 64; to++)
                        if ((moveBitboard & (1UL << to)) > 0)
                            if (moves < KnightMoveDistance[square][to])
                                KnightMoveDistance[square][to] = moves;

                }
            }
        }

        public Zero() {
            for (Int32 i = 0; i < generatedMoves.Length; i++)
                generatedMoves[i] = new Int32[MovesLimit];
            for (Int32 i = 0; i < pvMoves.Length; i++)
                pvMoves[i] = new Int32[PlyLimit];
            for (Int32 i = 0; i < killerMoves.Length; i++)
                killerMoves[i] = new Int32[KillerMovesAllocation];

            for (Int32 square = 0; square < 64; square++) {

                // Initialize queen to enemy king spatial value table. 
                QueenToEnemyKingSpatialValue[square] = new Int32[64];
                for (Int32 to = 0; to < 64; to++)
                    QueenToEnemyKingSpatialValue[square][to] = QueenDistanceToEnemyKingValue_[ChebyshevDistance[square][to]];

                // Initialize knight to enemy king spatial value table. 
                KnightToEnemyKingSpatialValue[square] = new Int32[64];
                for (Int32 to = 0; to < 64; to++)
                    KnightToEnemyKingSpatialValue[square][to] = KnightDistanceToEnemyKingValue_[RectilinearDistance[square][to]] + KnightMovesToEnemyKingValue_[KnightMoveDistance[square][to]];
            }
        }

        // Piece square tables. 
        private static readonly Int32[][] KingOpeningPositionValue =
        {
            new Int32[]
            {
                -25,-33,-33,-33,-33,-33,-33,-25,
                -25,-33,-33,-33,-33,-33,-33,-25,
                -25,-33,-33,-33,-33,-33,-33,-25,
                -25,-33,-33,-33,-33,-33,-33,-25,
                -25,-33,-33,-33,-33,-33,-33,-25,
                 -8,-17,-17,-17,-17,-17,-17, -8,
                 17, 17,  0,  0,  0,  0, 17, 17,
                 21, 25, 12,  0,  0, 12, 25, 21
            }, 
            new Int32[64]
        };

        private static readonly Int32[][] KingEndgamePositionValue =
        {
            new Int32[]
            {
                -42,-33,-25,-17,-17,-25,-33,-42,
                -33,-17, -8, -8, -8, -8,-17,-33,
                -25, -8, 17, 21, 21, 17, -8,-25,
                -25, -8, 21, 29, 29, 21, -8,-25,
                -25, -8, 21, 29, 29, 21, -8,-25,
                -25, -8, 17, 21, 21, 17, -8,-25,
                -33,-17, -8, -8, -8, -8,-17,-33,
                -42,-33,-25,-17,-17,-25,-33,-42
            }, 
            new Int32[64]
        };

        private static readonly Int32[][] QueenOpeningPositionValue =
        {
            new Int32[]
            {
                -17,-12, -8,  8,  0, -8,-12,-17,
                 -8,  0,  0,  0,  0,  0,  0, -8,
                 -8,  0,  4,  4,  4,  4,  0, -8,
                 -4,  0,  4,  4,  4,  4,  0, -4,
                 -4,  0,  4,  4,  4,  4,  0, -4,
                 -8,  0,  4,  4,  4,  4,  0, -8,
                 -8,  0,  0,  0,  0,  0,  0, -8,
                -17,-12, -8,  8,  0, -8,-12,-17
            }, 
            new Int32[64]
        };

        private static readonly Int32[][] RookPositionValue =
        {
            new Int32[]
            {
                  0,  0,  0,  0,  0,  0,  0,  0,
                  4,  8,  8,  8,  8,  8,  8,  4,
                 -4,  0,  0,  0,  0,  0,  0, -4,
                 -4,  0,  0,  0,  0,  0,  0, -4,
                 -4,  0,  0,  0,  0,  0,  0, -4,
                 -4,  0,  0,  0,  0,  0,  0, -4,
                 -4,  0,  0,  0,  0,  0,  0, -4,
                  0,  0,  4,  4,  4,  4,  0,  0
            }, 
            new Int32[64]
        };

        private static readonly Int32[][] BishopPositionValue =
        {
            new Int32[]
            {
                -17, -8, -8, -8, -8, -8, -8,-17,
                 -8,  0,  0,  0,  0,  0,  0, -8,
                 -8,  0,  4,  8,  8,  4,  0, -8,
                 -8,  4,  4,  8,  8,  4,  4, -8,
                 -8,  4, 12,  8,  8, 12,  4, -8,
                 -8, 12,  8, 12, 12,  8, 12, -8,
                 -8, 12,  0,  0,  0,  0, 12, -8,
                -17, -8, -8, -8, -8, -8, -8,-17
            }, 
            new Int32[64]
        };

        private static readonly Int32[][] KnightOpeningPositionValue =
        {
            new Int32[]
            {
                -25,-17,-17,-17,-17,-17,-17,-25,
                -17,-12,  0,  8,  8,  0,-12,-17,
                -17,  0,  8, 12, 12,  8,  0,-17,
                -17,  8, 12, 17, 17, 12,  8,-17,
                -17,  8, 12, 17, 17, 12,  8,-17,
                -17,  4,  8, 12, 12,  8,  4,-17,
                -17,-12,  0,  8,  8,  0,-12,-17,
                -25,-17,-17,-17,-17,-17,-17,-25
            }, 
            new Int32[64]
        };

        private static readonly Int32[][] PawnPositionValue =
        {
            new Int32[]
            {
                  0,  0,  0,  0,  0,  0,  0,  0,
                 75, 75, 75, 75, 75, 75, 75, 75,
                 25, 25, 29, 29, 29, 29, 25, 25,
                  4,  8, 12, 21, 21, 12,  8,  4,
                  0,  4,  8, 17, 17,  8,  4,  0,
                  4, -4, -8,  4,  4, -8, -4,  4,
                  4,  8,  8,-17,-17,  8,  8,  4,
                  0,  0,  0,  0,  0,  0,  0,  0
            }, 
            new Int32[64]
        };

        private static readonly Int32[][] PassedPawnEndgamePositionValue =
        {
            new Int32[]
            {
                  0,  0,  0,  0,  0,  0,  0,  0,
                100,100,100,100,100,100,100,100,
                 52, 52, 52, 52, 52, 52, 52, 52,
                 31, 31, 31, 31, 31, 31, 31, 31,
                 22, 22, 22, 22, 22, 22, 22, 22,
                 17, 17, 17, 17, 17, 17, 17, 17,
                  8,  8,  8,  8,  8,  8,  8,  8,
                  0,  0,  0,  0,  0,  0,  0,  0
            }, 
            new Int32[64]
        };
    }
}