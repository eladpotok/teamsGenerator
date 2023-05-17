import { InputNumber, Slider } from "antd"
import { useEffect, useState } from "react"
import { RxSlider } from "react-icons/rx"

import "./AppSlider.css";


function AppSlider(props) {

    const [currValue, setCurrValue] = useState(props.value[props.displayPath])

    useEffect(() => {
        (() => {
            setCurrValue(props.value[props.displayPath])
        })()
    })

    function onChangeHandler(value) {
        setCurrValue(value)
        props?.onChanged(value)
    }


    const minValue = props.minValue ? props.minValue : 0
    const maxValue = props.maxValue ? props.maxValue : 10

    return (
        <div style={{ display: 'flex', 'flex-direction': 'row', 'align-items': 'flex-end', 'justifyContent': 'flex-end' }}>
                                                    {/* <RxSlider size={14} style={{color: '#095c1f', marginBottom: '10px'}} /> */}
                                                    <Slider  value={currValue} onChange={onChangeHandler} className="ant-slider" railStyle={{backgroundColor: '#095c1f'}} trackStyle={{backgroundColor: '#95edad', color: 'green'}}   style={{flex: '1 1 auto' }} min={minValue} max={maxValue}  />
                                                    <InputNumber value={currValue} onChange={onChangeHandler}  min={1} max={10} style={{ margin: '0 4px' , width: '42px' }}   />
                                                  </div> 
    )
}

export default AppSlider