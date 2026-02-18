#!/usr/bin/env bash
declare -a __lash_argv=("$@")
greet() {
    local -a __lash_argv=("$@")
    local name="$1"
    local greeting="${2-}"
    if (( $# < 2 )); then greeting="Hello"; fi

    echo "${greeting}, ${name}"
    return 0
}
increment() {
    local -a __lash_argv=("$@")
    local count="$1"

    count=$(( ${count} + 1 ))
    echo ${count}
    return 0
}
values=("foo1" "bar2" "foo3" "bar4")
value_count=${#values[@]}
values+=("baz5")
newest_value=${values[4]}
readonly x=5
if (( 1 != 0 )); then
    echo "is positive"
fi
for i in $(seq 0 2 10); do
    echo $i
done
for thing in "${values[@]}"; do
    echo $thing
done
counter=0
while (( counter < 3 )); do
    counter=$(( ${counter} + 1 ))
    echo $counter
done

enum_value="ExampleEnumTypeOne"
if [[ ${enum_value} == "ExampleEnumTypeOne" ]]; then
    echo "enum match!"
fi
case ${counter} in
    1)
        echo "thing1"
        ;;
    2)
        echo "thing2"
        ;;
    3)
        echo "thing3"
        ;;
esac
emit() {
    local -a __lash_argv=("$@")
    echo "stdout-line"
    echo "stderr-line" 1>&2
}
emit >> "overview-out"
emit 2>> "overview-err"
emit &>> "overview-all"
word="Rob"
greeting=""
greeting=$(greet "${word}")
echo $greeting
first_arg=${__lash_argv[0]}
arg_count=${#__lash_argv[@]}
arg_summary="first arg: ${first_arg}, total args: ${arg_count}"
echo $arg_summary
__lash_shift_n=$(( 1 ))
if (( __lash_shift_n > 0 )); then __lash_argv=("${__lash_argv[@]:__lash_shift_n}"); fi
remaining_args=${#__lash_argv[@]}
shift_summary="after shift: ${remaining_args}"
echo $shift_summary
here=$(pwd)
folder=$(basename "${here}")
folder_summary="cwd folder: ${folder}"
echo $folder_summary
declare -A meta=()
meta["owner"]="lash"
meta["cwd"]=${folder}
owner=${meta["owner"]}
owner_summary="meta owner: ${owner}"
echo $owner_summary
case ${first_arg} in
    win-*)
        echo "input looks like windows"
        ;;
    linux-*)
        echo "input looks like linux"
        ;;
    "")
        echo "no first arg provided"
        ;;
esac
