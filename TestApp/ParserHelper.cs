using System;
using System.Linq;
using Pidgin;
using static Pidgin.Parser;

namespace TestApp
{
    /// Формат строки:
    ///     yyyy.MM.dd w HH:mm:ss.fff
    ///     yyyy.MM.dd HH:mm:ss.fff
    ///     HH:mm:ss.fff
    ///     yyyy.MM.dd w HH:mm:ss
    ///     yyyy.MM.dd HH:mm:ss
    ///     HH:mm:ss
    /// Где yyyy - год (2000-2100)
    ///     MM - месяц (1-12)
    ///     dd - число месяца (1-31 или 32). 32 означает последнее число месяца
    ///     w - день недели (0-6). 0 - воскресенье, 6 - суббота
    ///     HH - часы (0-23)
    ///     mm - минуты (0-59)
    ///     ss - секунды (0-59)
    ///     fff - миллисекунды (0-999). Если не указаны, то 0
    public static class ParserHelper
    {
        public static Parser<char, char> Asterisk { get; } = Char('*');
        public static Parser<char, int> NumberParser { get; } = UnsignedInt(10);

        public static Parser<char, (int begin, int? end)> IntervalParser { get; } =
            NumberParser.SelectMany(_ => Char('-').Then(NumberParser).Nullable(), (begin, end) => (begin, end));
        public static Parser<char, ScheduleFormatEntry> WholeIntervalParser { get; } =
            Asterisk.Map(_ => (begin: default(int?), end: default(int?)))
            .Or(IntervalParser.Map(x => ((int?)x.begin, x.end)))
            .Then(
                Char('/').Then(NumberParser).Nullable(),
                (interval, step) => new ScheduleFormatEntry(interval.begin, interval.end, step)
            );

        public static Parser<char, ScheduleFormatEntry[]> IntervalsSequenceParser { get; } =
            WholeIntervalParser.SeparatedAndOptionallyTerminatedAtLeastOnce(Char(',')).Map(x => x.ToArray())
                    .Assert(entries => !(entries.Length > 1 && entries.Any(x => x == ScheduleFormatEntry.Always)), $"Cannot have more than one wildcard entry in schedule")
            ;

        public static Parser<char, ScheduleDate> DateParser { get; } =
            IntervalsSequenceParser.AssertBounds("Year", Constant.MinYear, Constant.MaxYear).Then(
                Char('.').Then(
                    IntervalsSequenceParser.AssertBounds("Month", Constant.MinMonth, Constant.MaxMonth).Then(
                        Char('.').Then(
                            IntervalsSequenceParser. AssertBounds("Day", Constant.MinDay, Constant.MaxDay),
                            (_, days) => days
                        ),
                        (months, days) => (days: days, months: months)
                    ),
                    (_, md) => md
                 ),
                (years, md) => new ScheduleDate(years, md.months, md.days)
            );

        public static Parser<char, ScheduleFormatEntry[]> DayOfWeekParser { get; } =
            IntervalsSequenceParser.AssertBounds("Day of week", Constant.MinDayOfWeek, Constant.MaxDayOfWeek);

        public static Parser<char, ScheduleTime> TimeParser { get; } =
            IntervalsSequenceParser.AssertBounds("Hour", Constant.MinHour, Constant.MaxHour).Then(
                Char(':').Then(
                    IntervalsSequenceParser.AssertBounds("Min", Constant.MinMinute, Constant.MaxMinute).Then(
                        Char(':').Then(
                            IntervalsSequenceParser.AssertBounds("Sec", Constant.MinSec, Constant.MaxSec).Then(
                                Char('.').Then(IntervalsSequenceParser.AssertBounds("Millis", Constant.MinMillis, Constant.MaxMillis)).Nullable(),
                                (sec, millis) => (sec: sec, millis: millis)
                            ),
                            (_, sms) => sms
                        ),
                        (min, sms) => (min: min, sec: sms.sec, millis: sms.millis)
                    ),
                    (_, msms) => msms
                ),
                (hours, msms) => new ScheduleTime(hours, msms.min, msms.sec, msms.millis ?? new[] { ScheduleFormatEntry.SinglePoint(0) })
            );

        public static Parser<char, ScheduleFormat> FullFormatParser { get; } =
            Try(DateParser).Before(Char(' ')).Nullable().Then(
                Try(DayOfWeekParser.Before(Char(' '))).Nullable().Then(
                    TimeParser, (dayOfWeek, time) => (dayOfWeek: dayOfWeek, time: time)
                ),
                (date, dowt) => new ScheduleFormat(
                    date ?? new ScheduleDate(
                        new[] { ScheduleFormatEntry.Always },
                        new[] { ScheduleFormatEntry.Always },
                        new[] { ScheduleFormatEntry.Always }
                    ),
                    dowt.dayOfWeek ?? new[] { ScheduleFormatEntry.Always },
                    dowt.time
                )
            );

        public static Parser<char, ScheduleFormatEntry[]> AssertBounds(this Parser<char, ScheduleFormatEntry[]> parser, string formatPart, int min, int max) 
        {
            string msg = "";
            ScheduleFormatEntry? t;
            return parser.Assert(
                entries=>(msg = ((t = entries.FirstOrDefault(x => x.Begin < min || x.Begin > max || x.End < x.Begin || x.End > max)) != default ?
                    $"{formatPart} component ({t.Begin}, {t.End}) is out of bounds ({min}, {max}" : ""))== "", 
                msg);
        }

        public static Parser<TToken, T?> Nullable<TToken, T>(this Parser<TToken, T> parser) where T : struct
            => parser.Map<T?>(t => t).Or(Parser<TToken>.Return(default(T?)));
        public static Parser<TToken, T?> Nullable<TToken, T>(this Parser<TToken, T> parser, bool _ = true) where T : class
            => parser.Map<T?>(t => t).Or(Parser<TToken>.Return(default(T?)));
    }
}