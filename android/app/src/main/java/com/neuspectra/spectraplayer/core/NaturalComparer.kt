package com.neuspectra.spectraplayer.core

/**
 * Orders strings the way people expect for lecture files: digit runs compare by value
 * ("Lecture 2" before "Lecture 10"), everything else ignores case.
 */
object NaturalComparer : Comparator<String> {
    override fun compare(x: String, y: String): Int {
        var i = 0
        var j = 0
        while (i < x.length && j < y.length) {
            if (x[i].isAsciiDigit() && y[j].isAsciiDigit()) {
                val startX = i
                while (i < x.length && x[i].isAsciiDigit()) i++
                val startY = j
                while (j < y.length && y[j].isAsciiDigit()) j++

                val a = x.substring(startX, i).trimStart('0')
                val b = y.substring(startY, j).trimStart('0')
                if (a.length != b.length) return if (a.length < b.length) -1 else 1 // more digits = bigger number
                val c = a.compareTo(b)
                if (c != 0) return c

                // Same value ("1" vs "01"): fewer leading zeros first, so the order is stable.
                val zerosX = (i - startX) - a.length
                val zerosY = (j - startY) - b.length
                if (zerosX != zerosY) return if (zerosX < zerosY) -1 else 1
            } else {
                val c = x[i].uppercaseChar().compareTo(y[j].uppercaseChar())
                if (c != 0) return c
                i++
                j++
            }
        }
        return (x.length - i).compareTo(y.length - j)
    }

    private fun Char.isAsciiDigit() = this in '0'..'9'
}
