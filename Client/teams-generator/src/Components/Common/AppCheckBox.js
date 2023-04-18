import {  BorderOutlined, CheckOutlined, MinusOutlined } from "@ant-design/icons"
import { Button } from "antd"

function AppCheckBox(props) {
    function checkboxClickedHandler(value) {
        props.onChanged(value) 
    }

    return (
        <>
            {props.value &&  <Button onClick={()=>{checkboxClickedHandler(false)}} icon={<CheckOutlined />}/> }
            {!props.value &&   <Button icon={<MinusOutlined />}  onClick={()=>{checkboxClickedHandler(true)}}  />}
        </>
    )

}

export default AppCheckBox