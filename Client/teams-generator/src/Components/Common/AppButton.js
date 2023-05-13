import { Button } from 'antd'
import './AppButton.css'

function AppButton(props) {


        return (
            <button  class="button" onClick={props.onClick}>
                {props.children}
            </button>
        )

}

export default AppButton