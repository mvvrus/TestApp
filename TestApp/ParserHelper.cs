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
        public static Parser<char, Unit> Asterisk { get; } = Char('*').Map(_ => Unit.Value);
        public static Parser<char, int> NumberParser { get; } = Digit.AtLeastOnce().Map(s => int.Parse(new string(s.ToArray())));

        public static Parser<char, (int begin, int? end)> IntervalParser { get; } =
            NumberParser.SelectMany(_ => Char('-').Then(NumberParser).Optional().Map(MapMaybeStruct), (begin, end) => (begin, end));
        public static Parser<char, ScheduleFormatEntry> WholeIntervalParser { get; } =
            Asterisk.Map(_ => (begin: default(int?), end: default(int?)))
            .Or(IntervalParser.Map(x => ((int?)x.begin, x.end)))
            .SelectMany(
                _ => Char('/').Then(NumberParser).Optional().Map(MapMaybeStruct),
                (interval, step) => new ScheduleFormatEntry(interval.begin, interval.end, step)
            );

        public static Parser<char, ScheduleFormatEntry[]> IntervalsSequenceParser { get; } =
            Validate(WholeIntervalParser.SeparatedAndOptionallyTerminatedAtLeastOnce(Char(','))
                    .Map(x => x.ToArray()),
                GetWildcardsCheck());

        public static Parser<char, ScheduleDate> DateParser { get; } =
            Validate(IntervalsSequenceParser, GetBoundsCheck("Year", Constant.MinYear, Constant.MaxYear)).SelectMany(
                _=>Char('.').SelectMany(
                    _=> Validate(IntervalsSequenceParser, GetBoundsCheck("Month", Constant.MinMonth, Constant.MaxMonth)).SelectMany(
                        _=>Char('.').SelectMany(
                            _=>Validate(IntervalsSequenceParser, GetBoundsCheck("Day", Constant.MinDay, Constant.MaxDay)),
                            (_,days)=>days
                        ),
                        (months,days)=>(days:days,months:months)
                    ),
                    (_,md)=>md
                 ),
                (years,md)=>new ScheduleDate(years, md.months, md.days)
            );

        public static Parser<char, ScheduleFormatEntry[]> DayOfWeekParser { get; } =
            Validate(IntervalsSequenceParser, GetBoundsCheck("Day of week", Constant.MinDayOfWeek, Constant.MaxDayOfWeek));

        public static Parser<char, ScheduleTime> TimeParser { get; } =
            Validate(IntervalsSequenceParser, GetBoundsCheck("Hour", Constant.MinHour, Constant.MaxHour)).SelectMany(
                _ => Char(':').SelectMany(
                    _ => Validate(IntervalsSequenceParser, GetBoundsCheck("Min", Constant.MinMinute, Constant.MaxMinute)).SelectMany(
                        _=> Char(':').SelectMany(
                            _=> Validate(IntervalsSequenceParser, GetBoundsCheck("Sec", Constant.MinSec, Constant.MaxSec)).SelectMany(
                                _=> Char('.').Then(Validate(IntervalsSequenceParser, GetBoundsCheck("Millis", Constant.MinMillis, Constant.MaxMillis))).Optional().Map(MapMaybe),
                                (sec, millis) => (sec: sec, millis: millis)
                            ),  
                            (_,sms)=>sms
                        ),
                        (min,sms)=>(min:min,sec:sms.sec,millis:sms.millis)
                    ),
                    (_,msms)=>msms
                ),
                (hours, msms) => new ScheduleTime(hours, msms.min, msms.sec, msms.millis ?? new[] { ScheduleFormatEntry.SinglePoint(0) })
            );

        public static Parser<char, ScheduleFormat> FullFormatParser { get; } =
            Try(DateParser).Before(Char(' ')).Optional().Map(MapMaybe).SelectMany(
                _=> Try(DayOfWeekParser.Before(Char(' '))).Optional().Map(MapMaybe).SelectMany(
                    _=> TimeParser,(dayOfWeek,time)=> (dayOfWeek:dayOfWeek,time:time)
                ),
                (date,dowt)=> new ScheduleFormat(
                    date ?? new ScheduleDate(
                        new[] { ScheduleFormatEntry.Always },
                        new[] { ScheduleFormatEntry.Always },
                        new[] { ScheduleFormatEntry.Always }
                    ),
                    dowt.dayOfWeek ?? new[] { ScheduleFormatEntry.Always },
                    dowt.time
                )
            );

        private static Parser<char, ScheduleFormatEntry[]> Validate(Parser<char, ScheduleFormatEntry[]> parser,
            Func<ScheduleFormatEntry[], Parser<char, Unit>> check) =>
            parser.SelectMany(check, (entries, _) => entries);

        private static Func<ScheduleFormatEntry[], Parser<char, Unit>> GetWildcardsCheck() =>
            entries =>
            {
                if (entries.Length > 1 && entries.Any(x => x == ScheduleFormatEntry.Always))
                {
                    return Parser<char>.Fail<Unit>(
                        $"Cannot have more than one wildcard entry in schedule");
                }
                
                return Parser<char>.Return(Unit.Value);
            };
        
        private static Func<ScheduleFormatEntry[], Parser<char, Unit>> GetBoundsCheck(string formatPart, int min, int max) =>
            entries =>
            {
                foreach (var x in entries)
                {
                    if (x.Begin < min || x.Begin > max || x.End < x.Begin || x.End > max)
                    {
                        return Parser<char>.Fail<Unit>(
                            $"{formatPart} component ({x.Begin}, {x.End}) is out of bounds ({min}, {max})");
                    }
                }

                return Parser<char>.Return(Unit.Value);
            };

        private static T? MapMaybe<T>(Maybe<T> maybe) where T : class =>
            maybe.HasValue ? maybe.GetValueOrDefault() : null;
        
        private static T? MapMaybeStruct<T>(Maybe<T> maybe) where T : struct =>
            maybe.HasValue ? maybe.GetValueOrDefault() : null;
    }
}