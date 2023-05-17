import { Select } from "antd";
import { useEffect, useState } from "react";

const { Option } = Select;

function AppSelect(props) {

    const [values, setValues] = useState(props.value[props.displayPath])

    useEffect(() => {
        (() => {
            setValues(props.value[props.displayPath])
        })()
    })

    function selectChangedHandler(e) {
        const currValues = {}
        e.forEach(element => {
            currValues[element] = props.options[element]
        });
        
        console.log('setValues', currValues)
        setValues(currValues)
        props?.onChanged(currValues)
    }

    return (
        <Select style={{width: '100%'}} mode="multiple" placeholder="Select Shirts Colors" value={Object.keys(values)} onChange={selectChangedHandler} >
            {Object.keys(props.options).map(shirt => {
                return <Option value={shirt} label={shirt} />
            })}
        </Select>
    )
}

export default AppSelect;