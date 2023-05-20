function MyIconButton(props) {

    const isCircle = props.circle ?? false
    const buttonText = props.text
    const isDisabled = props.disabled ?? false

    const radius = isCircle ? '1000px' : '8px'
    const buttonStyle = {padding: '10px', borderWidth: '1px', borderStyle: 'solid', borderColor: 'white', borderRadius: radius, color: isDisabled ? 'grey' : 'white', background: 'rgba(9, 151, 73, 0.22)'};
    const deleteButtonStyle = {padding: '10px', borderWidth: '1px', borderStyle: 'solid', borderColor: 'rgba(255, 72, 40, 0.82)', color: isDisabled ? 'grey' : 'rgba(255, 72, 40, 0.82)', borderRadius: radius, background: 'rgba(255, 139, 153, 0.42)'}
    const primaryButtonStyle = {padding: '10px', borderWidth: '0px', borderStyle: 'solid', borderColor: 'white', borderRadius: radius, color: isDisabled ? 'grey' :'black', background: 'white'}
    const emptyStyle = {padding: '10px', borderWidth: '0px', borderRadius: radius, background: 'rgba(0, 255, 29, 0.0)', color: isDisabled ? 'grey' : 'black'}
    
    
    

    const styles = {
        deleteButton: deleteButtonStyle,
        primary: primaryButtonStyle,
        empty: emptyStyle,
    }



    const currStyle = Object.keys(styles).includes(props.buttonType) ? styles[props.buttonType] : buttonStyle

    if( props.shadow) {
        currStyle['box-shadow']= '3px 1px 20px 2px rgba(0,0,0,.3)'
    }

    return (
        <button style={currStyle} onClick={props.onClick}>
            <div>
                {props.icon}    
            </div>
            {buttonText && <div style={{fontSize: '10px'}}>
                {buttonText}
            </div>}
        </button>
    )
}

export default MyIconButton;