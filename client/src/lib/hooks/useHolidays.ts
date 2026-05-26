import { useQuery, type UseQueryOptions } from '@tanstack/react-query'
import { getHolidayCountries, getHolidays } from '../api/holidays'
import type { HolidayCountry, PublicHoliday } from '../types'
import { queryKeys } from './queryKeys'

type QueryOpts<TData> = Omit<
    UseQueryOptions<TData, Error, TData, readonly unknown[]>,
    'queryKey' | 'queryFn'
>

const ONE_HOUR = 60 * 60 * 1000
const ONE_DAY = 24 * 60 * 60 * 1000

export function useHolidays(year: number, options?: QueryOpts<PublicHoliday[]>) {
    return useQuery({
        queryKey: queryKeys.holidays(year),
        queryFn: () => getHolidays(year),
        staleTime: ONE_HOUR,
        ...options,
    })
}

export function useHolidayCountries(options?: QueryOpts<HolidayCountry[]>) {
    return useQuery({
        queryKey: queryKeys.holidayCountries,
        queryFn: getHolidayCountries,
        staleTime: ONE_DAY,
        ...options,
    })
}
