using System;
using System.Drawing;
using System.Linq;
namespace OpaGachi {
 static class SparklePalette {
 public static readonly string[] Names = { "Red", "Orange", "Yellow", "Green", "Blue", "Indigo", "Violet", "Dark Orange gradient", "Light Orange gradient", "Neon Orange gradient", "Blue-Pink-Green gradient", "Red-to-Black gradient", "Yellow-to-Black gradient", "Violet-Indigo-Black gradient", "White-to-Violet-to-Black gradient", "Black-Gray-White gradient", "Pearlescent", "Honey", "Silver", "Dark Silver", "Magnesium", "Gold", "Platinum", "Diamond" };
 static readonly int[][] Colors = {
 new[]{0xFF3030},new[]{0xFF8C00},new[]{0xFFE633},new[]{0x28CE70},new[]{0x268CFF},new[]{0x4B0082},new[]{0xAC55FF},
 new[]{0xFF8C00,0x9C3400},new[]{0xFFE0B0,0xFFA33B},new[]{0xFFFF36,0xFF6500},new[]{0x4295FF,0xFF8BD9,0x6DFCA6},new[]{0xFF3030,0x090909},new[]{0xFFED28,0x090909},new[]{0xAF52DE,0x4B0082,0x090909},new[]{0xFFFFFF,0xA350DA,0x090909},new[]{0x090909,0x999999,0xFFFFFF},
 new[]{0xFFFFFF,0xFFC9E8,0xB8EEF2,0xD5C6FF},new[]{0xFFF0AA,0xEBA822,0x8C4E00},new[]{0xFFFFFF,0xA9AEB6,0x60666E},new[]{0x92969E,0x34383D,0xCFD2DA},new[]{0xD9DEE0,0x7E8589,0xBBC3C7},new[]{0xFFF1A0,0xD4AF37,0x805300},new[]{0xFFFFFF,0xB5C5CC,0xE5E4E2},new[]{0xFFFFFF,0xB8F6FF,0xD7CBFF,0xFFFFFF} };
 public static Color[] Stops(string name) { int i=Array.IndexOf(Names,name); return i<0 ? NameFill.Stops(name) : Colors[i].Select(Theme.Hex).ToArray(); }
 }
}
